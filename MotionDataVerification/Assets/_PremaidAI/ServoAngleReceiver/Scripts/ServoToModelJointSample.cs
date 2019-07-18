using System;
using UnityEngine;
using PreMaid.RemoteController;

namespace PreMaid.ServoAngleReceiver
{
    /// <summary>サーボ角度をロボットモデルにFKでアタッチするサンプル</summary>
    /// <remarks>
    /// <see cref="PreMaid.HumanoidTracer.HumanoidModelJoint"/>の逆の操作をやるようなイメージ
    /// </remarks>
    public class ServoToModelJointSample : MonoBehaviour
    {
        [Serializable]
        class TargetServo
        {
            [SerializeField] private PreMaidServo.ServoPosition _servoPosition;
            [SerializeField] private ModelJoint.Axis _axis;
            [SerializeField] private bool _inverse;

            private float _defaultServoPosition;

            public PreMaidServo.ServoPosition ServoPosition => _servoPosition;

            /// <summary>このサーボの回転軸を取得します。</summary>
            public ModelJoint.Axis Axis => _axis;

            /// <summary>このサーボの回転角度を取得します。</summary>
            public float AngleDegree { get; private set; }

            /// <summary>このサーボの回転を取得します。</summary>
            public Quaternion Rotation { get; private set; }

            public bool HasValidAngle { get; set; }

            private Quaternion _initialLocalRotation = Quaternion.identity;
            private Vector3 _localServoAxis = Vector3.right;

            /// <summary>デフォルトでのサーボエンコーダ値を、サーボの位置にもとづいて初期化する。</summary>
            public void Initialize(ServoToModelJointSample component)
            {

                _defaultServoPosition = 
                    (_servoPosition == PreMaidServo.ServoPosition.RightShoulderRoll) ? 9500 :
                    (_servoPosition == PreMaidServo.ServoPosition.LeftShoulderRoll) ? 5500 :
                    7500;

                AngleDegree = 0;
                Rotation = Quaternion.identity;

                //ModelJointと同じで、もとのボーンの向きに依存させないために補正
                Vector3 axis =
                    (_axis == ModelJoint.Axis.X) ? Vector3.right :
                    (_axis == ModelJoint.Axis.Y) ? Vector3.up :
                    Vector3.forward;

                var rootTransform = component.GetModelRoot();
                _initialLocalRotation = component.transform.localRotation;
                _localServoAxis = Quaternion.Inverse(component.transform.rotation) * (rootTransform.rotation * axis);
            }

            /// <summary>
            /// サーボ角度の値を受け取ってモデルで再現すべき角度値に変換する。
            /// </summary>
            /// <param name="servoData">シリアル通信で得たサーボ値</param>
            public void ReceiveServoValue(ReceivedServoData servoData)
            {
                if (servoData.ServoPosition != _servoPosition)
                {
                    //普通来ない(ようにする)
                    Debug.Log("サーボ番号が一致しません");
                    return;
                }

                float servoAngleDegree = (servoData.Value - _defaultServoPosition) * 135 / 4000;

                //NOTE: ここをもっと細分化する必要があればプロパティ化
                switch (_servoPosition)
                {
                    case PreMaidServo.ServoPosition.LeftShoulderRoll:
                        servoAngleDegree -= 66;
                        break;
                    case PreMaidServo.ServoPosition.RightShoulderRoll:
                        servoAngleDegree += 66;
                        break;
                    default:
                        break;
                }

                //[-180, 180]に収める
                servoAngleDegree = Mathf.Repeat(servoAngleDegree, 360);
                if (servoAngleDegree > 180)
                {
                    servoAngleDegree -= 360;
                }

                if (_inverse)
                {
                    servoAngleDegree *= -1;
                }

                AngleDegree = servoAngleDegree;
                //NOTE: ここは1ボーンに1サーボだけが対応する前提の実装であることに注意
                Rotation = _initialLocalRotation * Quaternion.AngleAxis(AngleDegree, _localServoAxis);
                HasValidAngle = true;
            }

        }

        //NOTE: 単一ボーンに複数サーボが対応するケースを考慮して配列化してたが、今のところ1要素の場合のみ動く
        [SerializeField]
        [Tooltip("このジョイントに対応するサーボを、根本側から順番に設定する。")]
        private TargetServo[] _targetServos = new TargetServo[1];

        [SerializeField]
        private float _angleLerpFactor = 0.1f;

        private PreMaidServoAngleReceiver _receiver = null;

        /// <summary>サーボ角度の取得元を設定します。</summary>
        /// <param name="receiver"></param>
        public void RegisterReceiver(PreMaidServoAngleReceiver receiver)
        {
            if (_receiver != null)
            {
                _receiver.ServoDataUpdated -= OnServoDataUpdated;
            }

            _receiver = receiver;
            if (_receiver != null)
            {
                _receiver.ServoDataUpdated += OnServoDataUpdated;
            }
        }

        private void OnServoDataUpdated(int page)
        {
            for (int i = 0; i < _targetServos.Length; i++)
            {
                var pos = _targetServos[i].ServoPosition;
                //自身のサーボが更新されたほうのサーボ群に含まれてるかチェック
                if ((page == 0 && (int)pos < 0x10) ||
                    (page == 1 && (int)pos > 0x0F)
                    )
                {
                    _targetServos[i].ReceiveServoValue(
                        _receiver.ServoAngles[pos]
                        );
                }
            }
        }

        private void Start()
        {
            foreach (var servo in _targetServos)
            {
                servo.Initialize(this);
            }
        }

        void Update()
        {
            Quaternion localRotation = Quaternion.identity;
            bool hasValidAngle = false;
            //root側からジョイント指定するため、乗算は逆順にやった方が良いハズ
            //※いまのところ2要素以上の実装で使ってない
            for (int i = _targetServos.Length - 1; i >= 0; i--)
            {
                hasValidAngle = hasValidAngle || _targetServos[i].HasValidAngle;
                localRotation = _targetServos[i].Rotation * localRotation;
            }

            if (hasValidAngle)
            {
                transform.localRotation =
                    Quaternion.Slerp(
                        transform.localRotation,
                        localRotation,
                        _angleLerpFactor * 60 * Time.deltaTime
                        );
            }
        }

        private Transform GetModelRoot()
        {
            Transform t = transform;
            //条件文を逆に言うと、親が無いか、アニメーターがあれば終点扱い
            while (t.parent != null && t.GetComponent<Animator>() == null)
            {
                t = t.parent;
            }
            return t;
        }
    }
}