using System;
using static PreMaid.RemoteController.PreMaidServo;

namespace PreMaid.ServoAngleReceiver
{
    /// <summary>通信越しで取得可能な、プリメイドAIのサーボ角度に関するデータ</summary>
    public class ReceivedServoData
    {
        // バイナリとして送られてくるデータのサイズ
        private const int StructByteLength = 14;

        public ReceivedServoData(ServoPosition pos)
        {
            ServoPosition = pos;
        }

        private ReceivedServoData(ServoPosition pos, bool isValid, short rawValue, short offset, short operationValue)
            : this(pos)
        {
            IsValid = isValid;
            RawValue = rawValue;
            Offset = offset;
            OperationValue = operationValue;
        }

        /// <summary>サーボ番号</summary>
        public ServoPosition ServoPosition { get; }

        /// <summary>
        /// 値が有効かどうかを取得します。<see cref="ServoPosition"/>が変な値の場合は無効になる。(起動直後も無効にかも)
        /// </summary>
        public bool IsValid { get; private set; } = false;

        /// <summary>現在のエンコーダ値。基本的にはコレではなく<see cref="Value"/>を用いる</summary>
        public short RawValue { get; private set; } = 0;

        /// <summary>キャリブレーションのオフセットにあたる値。</summary>
        public short Offset { get; private set; } = 0;

        /// <summary>指令された、目標となるサーボ角度。脱力時は0になる。</summary>
        public short OperationValue { get; private set; } = 0;

        /// <summary>エンコーダ値からオフセットを除去した値。基本的にはこの値を用いる</summary>
        public short Value => (short)(RawValue - Offset);

        /// <summary>
        /// 文字列表現のシリアル通信メッセージと読み込み位置のオフセットを指定して、サーボのデータを更新します。
        /// </summary>
        /// <param name="message">シリアル通信で得たメッセージ</param>
        /// <param name="offset">読込開始オフセット</param>
        public void UpdateFromString(string message, int offset)
        {
            //14バイトぶん読む
            if (message.Length < offset + StructByteLength * 2)
            {
                return;
            }

            var bytes = new byte[StructByteLength];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(message.Substring(offset + i * 2, 2), 16);
            }
            UpdateFromBytes(bytes, 0);
        }

        /// <summary>
        /// バイナリのシリアル通信メッセージとオフセットを指定して、サーボのデータを更新します。
        /// </summary>
        /// <param name="message">シリアル通信で得たメッセージ</param>
        /// <param name="offset">読込開始オフセット</param>
        public void UpdateFromBytes(byte[] message, int offset)
        {
            if (message.Length < offset + StructByteLength)
            {
                return;
            }

            if (message[offset] != (byte)ServoPosition)
            {
                //このサーボのデータではない = 無視
                return;
            }

            IsValid = (message[offset + 1] & 0x80) != 0;

            RawValue = ParseShort(message, offset + 2);
            Offset = ParseShort(message, offset + 4);
            OperationValue = ParseShort(message, offset + 6);

            //NOTE: [12]バイト目が速度、[13]バイト目がストレッチなのが把握済みだが、今使わないので捨てる
        }

        /// <summary>
        /// シリアル通信のバイナリからデータを新規作成します。
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        public static ReceivedServoData CreateFromBytes(byte[] bytes, int startIndex)
        {
            if (bytes == null || bytes.Length < startIndex + StructByteLength)
            {
                return InvalidServoData();
            }

            var result = new ReceivedServoData((ServoPosition)bytes[startIndex]);
            result.UpdateFromBytes(bytes, startIndex);
            return result;
        }

        /// <summary>無効なサーボデータを取得します。</summary>
        /// <returns></returns>
        public static ReceivedServoData InvalidServoData()
            => new ReceivedServoData(ServoPosition.HeadPitch, false, 0, 0, 0);

        //符号つき2バイト整数を読み取る。
        private static short ParseShort(byte[] data, int offset)
        {
            //NOTE: BitConverterを使わないのはCPUのエンディアンを無視するため
            int intResult = data[offset] + (data[offset + 1] << 8);
            return (short)(intResult < 32768 ? intResult : -(65536 - intResult));
        }
    }
}
