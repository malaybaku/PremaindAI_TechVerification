using System.Linq;
using System.IO.Ports;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PreMaid.ServoAngleReceiver
{
    /// <summary>
    /// UIのセットアップを行うクラス。
    /// やりたいことは<see cref="RemoteController.PreMaidRemoteControlView"/>とほぼ同じ
    /// </summary>
    public class ServoAngleReaderUiInitialize : MonoBehaviour
    {
        [SerializeField]
        private PreMaidSubprocessRemoteController _preMaidController = null;

        [SerializeField]
        private PreMaidServoAngleReceiver _angleReceiver = null;

        [SerializeField]
        private TMP_Dropdown dropdown = null;

        [SerializeField]
        private Button openPortButton = null;

        [SerializeField]
        private Button servoOffButton = null;

        [SerializeField]
        private Button copyPoseButton = null;

        [SerializeField]
        private Toggle continuousToggle = null;

        private void Start()
        {
            if (_preMaidController == null ||
                _angleReceiver == null || 
                dropdown == null || 
                openPortButton == null ||
                servoOffButton == null || 
                copyPoseButton == null || 
                continuousToggle == null
                )
            {
                Debug.Log("セットアップに必要なコンポーネントが指定されていません。");
                return;
            }

            dropdown.AddOptions(
                SerialPort.GetPortNames()
                    .Select(v => new TMP_Dropdown.OptionData(v))
                    .ToList()
                );

            openPortButton.onClick.AddListener(() =>
            {
                _preMaidController.OpenSerialPort(
                    dropdown.options[dropdown.value].text
                    );
            });

            servoOffButton.onClick.AddListener(() =>
            {
                //脱力状態で続けたい = デフォルト動作と違う事に注意
                _preMaidController.ForceAllServoStop(false);
            });

            copyPoseButton.onClick.AddListener(() =>
            {
                //HACK: ちょくちょく受信データが汚くなるのでひとまず受信直前に捨てる方針。通信速度が上がってきたらココ外す必要あり
                _preMaidController.ResetBuffer();
                _angleReceiver.RequestReadServoAngles();
            });

            continuousToggle.onValueChanged.AddListener(b =>
            {
                _angleReceiver.SetContinuousMode(b);
            });

            _angleReceiver.OnContinuousModeChange += b =>
            {
                continuousToggle.isOn = b;
            };
        }
    }
}
