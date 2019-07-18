using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using static PreMaid.RemoteController.PreMaidServo;

namespace PreMaid.ServoAngleReceiver
{
    /// <summary>サーボ角度を取得するスクリプト</summary>
    [RequireComponent(typeof(PreMaidSubprocessRemoteController))]
    public class PreMaidServoAngleReceiver : MonoBehaviour
    {
        //角度の取得周期[sec]
        //note: 全サーボのデータを取得すると約500byteを受信するため、ボーレート(115200bit/s, 約10kbyte/s)との整合性に注意。
        private const float ReadServoAngleIntervalSec = 0.2f;

        /// <summary>現在のサーボ情報を取得します。</summary>
        internal IReadOnlyDictionary<ServoPosition, ReceivedServoData> ServoAngles => _servoAngles;

        /// <summary>連続送信モードが切り替わったときに発火します。</summary>
        public event Action<bool> OnContinuousModeChange;

        /// <summary>サーボ角度が更新されたときに発火します。引数は更新されたサーボのサーボID範囲(0ならサーボ0x00～0x0F、1なら0x10～0x1F)</summary>
        public event Action<int> ServoDataUpdated;

        //これがtrueのときは自動で角度を取得し続ける、デフォルトではfalse
        private bool _continuousMode = false;

        //自動の姿勢取得で使うタイマー
        private float _timer = 0.0f;

        //次に角度を取得するサーボ群を示すページ番号。
        //NOTE: ホントは全サーボのデータが一気に欲しいが、通信が詰まると挙動が怪しくなるため、時間をあけて約半数ずつサーボを更新
        private int _nextTargetPage = 0;

        private PreMaidSubprocessRemoteController _controller = null;

        private Dictionary<ServoPosition, ReceivedServoData> _servoAngles = null;

        /// <summary>連続モードの有効/無効を更新します。</summary>
        /// <param name="newValue"></param>
        public void SetContinuousMode(bool newValue)
        {
            if (_continuousMode == newValue)
            {
                return;
            }

            Debug.Log("連続モード : " + newValue.ToString());
            _continuousMode = newValue;
            OnContinuousModeChange?.Invoke(_continuousMode);

            if (newValue)
            {
                _timer = 0;
            }
        }

        /// <summary>サーボ値の単発読み取りをリクエストします。連続モードでは呼び出されても何もしません。</summary>
        public void RequestReadServoAngles()
        {
            if (_continuousMode)
            {
                return;
            }
            _controller.RequestReadServoAngles(_nextTargetPage);
            _nextTargetPage = (_nextTargetPage == 0) ? 1 : 0;
        }

        private void Start()
        {
            _servoAngles = new Dictionary<ServoPosition, ReceivedServoData>();
            foreach (var pos in Enum
                .GetValues(typeof(ServoPosition))
                .Cast<ServoPosition>()
                )
            {
                _servoAngles[pos] = new ReceivedServoData(pos);
            }

            _controller = GetComponent<PreMaidSubprocessRemoteController>();
            _controller.OnReceivedMessage += OnReceivedFromPreMaidAI;
        }

        private void Update()
        {
            if (_continuousMode)
            {
                _timer += Time.deltaTime;
                if (_timer > ReadServoAngleIntervalSec)
                {
                    _controller.RequestReadServoAngles(_nextTargetPage);
                    _nextTargetPage = (_nextTargetPage == 0) ? 1 : 0;

                    _timer -= ReadServoAngleIntervalSec;
                }
            }
        }

        private void OnReceivedFromPreMaidAI(string message)
        {
            //粗いチェック: コマンド長、コマンド種類、およびエラービット
            if (message.Substring(0, 2) != "E4" || 
                message.Substring(2, 2) != "01" ||
                message.Substring(4, 2) != "00" ||
                message.Length < 456
                )
            {
                return;
            }

            //細かいチェック: サーボデータの形かどうか。
            //  この追加チェックをする理由は、0x01コマンドで来る可能性があるデータが他にもあるため(バッテリ残量等)
            //  期待する入力文字列はこんな感じ(※スペースは実際には含まれない)
            //  E4 01 00 [00 ..(00のサーボデータ)..] [01 ..(01のサーボデータ)..] .. [0F ..(0Fのサーボデータ)..] XX
            for(int i = 0; i < 16; i++)
            {
                //サーボデータ1つで14バイト -> 28文字ずつ飛ばすと00,01,02,..か10,11,12,..のどちらかの並び
                string maybeServoId = message.Substring(6 + i * 28, 2);
                string servoIdUpperPage = i.ToString("X2");
                string servoIdLowerPage = (i + 16).ToString("X2");

                if (maybeServoId != servoIdUpperPage &&
                    maybeServoId != servoIdLowerPage)
                {
                    return;
                }
            }

            //メインの読み込み: 14バイトずつ読み込む。
            for (int i = 0; i < 16; i++)
            {
                int offset = 6 + i * 28;
                var servoPosition = (ServoPosition)Convert.ToByte(message.Substring(offset, 2), 16);

                if (_servoAngles.ContainsKey(servoPosition))
                {
                    _servoAngles[servoPosition].UpdateFromString(message, offset);
                }
            }

            //このメッセージが持ってきたサーボのデータ群が0x00～0x0Fの範囲なのか、0x10～0x1Fの範囲なのかをチェック
            int pageNumber = (message.Substring(6, 2) == "10") ? 1 : 0;
            ServoDataUpdated?.Invoke(pageNumber);
        }
    }
}
