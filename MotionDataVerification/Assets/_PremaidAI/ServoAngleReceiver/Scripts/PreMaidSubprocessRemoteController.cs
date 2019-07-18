using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.IO;
using UnityEngine;

namespace PreMaid.ServoAngleReceiver
{
    /// <summary>
    /// .NET Frameworkで動くサブコンソールプロセスを介してプリメイドAIを制御するためのコントローラ
    /// </summary>
    /// <remarks>
    /// このクラスはC# 縛りでUnityのMonoのシリアル通信実装問題を避けるために作っている。
    /// コレを使う場合、サブプロセス用のビルドが必要、かつUnityプロセスから直接シリアルにつなぐ事は出来なくなる点に注意
    /// </remarks>
    public class PreMaidSubprocessRemoteController : MonoBehaviour
    {
        [SerializeField]
        private string SubprocessExeFilePath = "";

        private ConcurrentQueue<string> _receivedMessage = new ConcurrentQueue<string>();

        private Process _serialCommunicationProcess = null;
        private Thread _serialReadingThread = null;

        private bool HasValidProcess => _serialCommunicationProcess != null;

        /// <summary>プリメイドAIからメッセージを取得すると発火します。</summary>
        /// <remarks>
        /// 引数の形式は<see cref="RemoteController.PreMaidController.OnReceivedFromPreMaidAI"/>と同じ
        /// </remarks>
        public event Action<string> OnReceivedMessage;

        private string _messageBuffer = "";

        /// <summary>シリアル通信用のサブプロセスを起動します。</summary>
        public void ActivateSubprocess()
        {
            if (_serialCommunicationProcess != null)
            {
                UnityEngine.Debug.Log("サブプロセスは起動済みです。");
                return;
            }
            else if(!File.Exists(SubprocessExeFilePath))
            {
                UnityEngine.Debug.Log("サブプロセスの実行ファイルが見つかりません: " + SubprocessExeFilePath);
                return;
            }
            
            var info = new ProcessStartInfo()
            {
                FileName = SubprocessExeFilePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            _serialCommunicationProcess = Process.Start(info);
            _serialReadingThread = new Thread(ReceiveThread);
            _serialReadingThread.Start();
        }

        /// <summary>
        /// 通信用のサブプロセスを終了します。
        /// </summary>
        /// <remarks>
        /// OnDestroyでも呼ばれる
        /// </remarks>
        public void ShutdownSubProcess()
        {
            if (_serialCommunicationProcess != null)
            {
                _serialCommunicationProcess.StandardInput.WriteLine("QUIT:");
                _serialCommunicationProcess = null;
            }

            if (_serialReadingThread != null)
            {
                //NOTE: マナー悪いが、コンソール読込が止まるだけなので危険度は低め
                _serialReadingThread.Abort();
                _serialReadingThread = null;
            }
        }

        /// <summary>
        /// シリアルポート名を指定して通信を開始します。
        /// </summary>
        /// <param name="portName"></param>
        /// <remarks>
        /// OnDestroyまでには閉じる
        /// </remarks>
        public void OpenSerialPort(string portName)
        {
            if (!HasValidProcess)
            {
                ActivateSubprocess();
                //return;
            }

            _serialCommunicationProcess.StandardInput.WriteLine("OPEN:" + portName);
        }

        /// <summary>
        /// プリメイドAIにメッセージを送信します。
        /// </summary>
        /// <param name="message"></param>
        public void Send(string message)
        {
            if (HasValidProcess)
            {
                _serialCommunicationProcess.StandardInput.WriteLine("SEND:" + message);
            }
        }

        /// <summary>
        /// 全サーボの強制脱力命令
        /// </summary>
        /// <param name="disconnect">命令後に切断するかどうか。プリメイドAIの動作に精通していない限り、デフォルトのtrueで呼ぶ事でサーボが壊れるのを防ぐ</param>
        public void ForceAllServoStop(bool disconnect = true)
        {
            string allStop =
                "50 18 00 06 02 00 00 03 00 00 04 00 00 05 00 00 06 00 00 07 00 00 08 00 00 09 00 00 0A 00 00 0B 00 00 0C 00 00 0D 00 00 0E 00 00 0F 00 00 10 00 00 11 00 00 12 00 00 13 00 00 14 00 00 15 00 00 16 00 00 17 00 00 18 00 00 1A 00 00 1C 00 00 FF";
            Send(PreMaidUtility.RewriteXorString(allStop));

            if (disconnect)
            {
                ShutdownSubProcess();
            }
        }

        /// <summary>
        /// サーボ角度の取得リクエストを送信します。
        /// </summary>
        /// <param name="page">
        /// ページ番号。0を指定するとサーボIDが0x00～0x0F、1を指定するとサーボIDが0x10～0x1Fの範囲のサーボ情報を取得する。
        /// </param>
        public void RequestReadServoAngles(int page)
        {
            string indexOffsetString = (page == 1 ? "10" : "00");
            string servoAngleRequest = "07 01 00 05 " + indexOffsetString + " E0 FF";
            UnityEngine.Debug.Log("リクエスト:" + servoAngleRequest);
            Send(PreMaidUtility.RewriteXorString(servoAngleRequest));
        }

        /// <summary>シリアル通信の受信状況をリセットします。</summary>
        /// <remarks>
        /// 明らかに通信異常が出ている場合に呼び出します。
        /// </remarks>
        public void ResetBuffer()
        {
            _messageBuffer = "";
        }

        private void Update()
        {
            if (!HasValidProcess)
            {
                return;
            }

            if (!_receivedMessage.IsEmpty && 
                _receivedMessage.TryDequeue(out string message))
            {
                AddMessageToBuffer(message);
            }
        }

        private void ReceiveThread()
        {
            while (true)
            {
                string line = _serialCommunicationProcess.StandardOutput.ReadLine();
                _receivedMessage.Enqueue(line);
            }
        }

        private void AddMessageToBuffer(string message)
        {
            _messageBuffer += message;
            if (_messageBuffer.Length < 2)
            {
                //まだ足りない
                return;
            }

            if (_messageBuffer.StartsWith("00"))
            {
                //明らかにバッファが壊れてる
                _messageBuffer = "";
                return;
            }

            int messageLength = Convert.ToByte(_messageBuffer.Substring(0, 2), 16) * 2;
            if (_messageBuffer.Length < messageLength)
            {
                //まだ足りない
                return;
            }

            //1フレーム最大1メッセージまでとする。これはそんなに問題にはならないハズ
            string messageToFire = _messageBuffer.Substring(0, messageLength);
            OnReceivedMessage(messageToFire);

            _messageBuffer = _messageBuffer.Substring(messageLength);
        }


        private void OnDestroy()
        {
            UnityEngine.Debug.Log("Shut down sub process.");
            ShutdownSubProcess();
        }
    }

}
