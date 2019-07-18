using UnityEngine;

namespace PreMaid.ServoAngleReceiver
{
    /// <summary>
    /// <see cref="PreMaidServoAngleReceiver"/>と<see cref="ServoToModelJointSample"/>を接続するクラス
    /// </summary>
    public class ServoToModelJointConnector : MonoBehaviour
    {
        [SerializeField]
        private PreMaidServoAngleReceiver _servoAngleReceiver = null;

        [SerializeField]
        private ServoToModelJointSample[] _servoToModelJoint = null;

        void Start()
        {
            foreach (var servoToJoint in _servoToModelJoint)
            {
                servoToJoint.RegisterReceiver(_servoAngleReceiver);
            }
        }
    }
}
