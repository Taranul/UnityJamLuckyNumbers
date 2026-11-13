using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VContainer;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Manages the Debug HUD and interactive UI buttons for icosahedron rotation.
    /// </summary>
    public class DebugRotationUI : MonoBehaviour
    {
        [Header("Target")]
        [FormerlySerializedAs("controller")]
        [SerializeField] private IcosahedronController _controller;

        [Header("Button References")]
        [FormerlySerializedAs("upButton")]
        [SerializeField] private HoldButton _upButton;

        [FormerlySerializedAs("downButton")]
        [SerializeField] private HoldButton _downButton;

        [FormerlySerializedAs("leftButton")]
        [SerializeField] private HoldButton _leftButton;

        [FormerlySerializedAs("rightButton")]
        [SerializeField] private HoldButton _rightButton;

        [Header("Roll Status HUD")]
        [FormerlySerializedAs("rollStatusText")]
        [SerializeField] private Text _rollStatusText;

        [Inject]
        public void Construct(IcosahedronController controller)
        {
            _controller = controller;
        }

        private void Awake()
        {
            if (_controller == null)
            {
                _controller = FindFirstObjectByType<IcosahedronController>();
            }

            EnsureEventSystem();

            if (_upButton == null || _downButton == null || _leftButton == null || _rightButton == null || _rollStatusText == null)
            {
                BuildUI();
            }
        }

        private void Update()
        {
            HandleModeToggleInput();
            UpdateStatusHUD();
        }

        private void HandleModeToggleInput()
        {
            if (_controller == null) return;
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.mKey.wasPressedThisFrame || kb.tKey.wasPressedThisFrame))
            {
                _controller.ToggleRollMode();
            }
        }

        private void UpdateStatusHUD()
        {
            if (_rollStatusText == null || _controller == null) return;

            string modeName = _controller.CurrentRollMode == RollMode.SmoothCenterRoll ? "Smooth Roll" : "Edge-Pivot Tumble";
            float speed = _controller.CurrentTravelVelocity.magnitude;

            if (_controller.IsRestingFlat)
            {
                _rollStatusText.text = $"<b>DND ROLL: #{_controller.CurrentTopFaceNumber}</b>  (<color=#55ff88>RESTING FLAT</color>) | <color=#66ddff>[{modeName} (Press 'M' to Toggle)]</color>\n" +
                                      $"<size=14><color=#dddddd>Floor Face: #{_controller.CurrentBottomFaceNumber} | Tilt: {_controller.CurrentTiltAngle:F0}° | Speed: {speed:F1} m/s</color></size>";
            }
            else
            {
                int effortPct = Mathf.RoundToInt(_controller.CurrentRollEffort * 100f);
                string aimText = _controller.TargetTopFaceNumber > 0 ? $"Aiming: #{_controller.TargetTopFaceNumber}" : "Tilting";
                _rollStatusText.text = $"<b>ROLLING...</b>  (<color=#ffcc44>{aimText} | Effort: {effortPct}%</color>) | <color=#66ddff>[{modeName} (Press 'M' to Toggle)]</color>\n" +
                                      $"<size=14><color=#dddddd>Current Top: #{_controller.CurrentTopFaceNumber} | Floor: #{_controller.CurrentBottomFaceNumber} | Tilt: {_controller.CurrentTiltAngle:F0}° | Speed: {speed:F1} m/s</color></size>";
            }
        }

        private void EnsureEventSystem()
        {
            EventSystem es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<InputSystemUIInputModule>();
            }
            else if (es.GetComponent<InputSystemUIInputModule>() == null && es.GetComponent<StandaloneInputModule>() == null)
            {
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        [ContextMenu("Build UI Hierarchy")]
        public void BuildUI()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            Font standardFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (standardFont == null)
            {
                standardFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            GameObject hudObj = GameObject.Find("RollStatusBanner");
            if (hudObj == null)
            {
                hudObj = new GameObject("RollStatusBanner");
                hudObj.transform.SetParent(transform, false);
            }

            RectTransform hudRt = hudObj.GetComponent<RectTransform>();
            if (hudRt == null) hudRt = hudObj.AddComponent<RectTransform>();
            hudRt.anchorMin = new Vector2(0.5f, 1f);
            hudRt.anchorMax = new Vector2(0.5f, 1f);
            hudRt.pivot = new Vector2(0.5f, 1f);
            hudRt.anchoredPosition = new Vector2(0f, -20f);
            hudRt.sizeDelta = new Vector2(700f, 90f);

            Image hudBg = hudObj.GetComponent<Image>();
            if (hudBg == null) hudBg = hudObj.AddComponent<Image>();
            hudBg.color = new Color(0.06f, 0.08f, 0.12f, 0.85f);

            GameObject textObj = hudObj.transform.Find("Text")?.gameObject;
            if (textObj == null)
            {
                textObj = new GameObject("Text");
                textObj.transform.SetParent(hudObj.transform, false);
            }
            RectTransform textRt = textObj.GetComponent<RectTransform>();
            if (textRt == null) textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = new Vector2(-20f, -10f);

            Text textComp = textObj.GetComponent<Text>();
            if (textComp == null) textComp = textObj.AddComponent<Text>();
            textComp.font = standardFont;
            textComp.fontSize = 20;
            textComp.alignment = TextAnchor.MiddleCenter;
            textComp.color = Color.white;
            textComp.supportRichText = true;
            _rollStatusText = textComp;

            GameObject panel = transform.Find("ControlPadPanel")?.gameObject;
            if (panel == null)
            {
                panel = new GameObject("ControlPadPanel");
                panel.transform.SetParent(transform, false);
            }

            RectTransform panelRt = panel.GetComponent<RectTransform>();
            if (panelRt == null) panelRt = panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(1f, 0f);
            panelRt.anchorMax = new Vector2(1f, 0f);
            panelRt.pivot = new Vector2(1f, 0f);
            panelRt.anchoredPosition = new Vector2(-40f, 40f);
            panelRt.sizeDelta = new Vector2(240f, 240f);

            Image panelBg = panel.GetComponent<Image>();
            if (panelBg == null) panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0.08f, 0.08f, 0.12f, 0.75f);

            GameObject titleObj = panel.transform.Find("Title")?.gameObject;
            if (titleObj == null)
            {
                titleObj = new GameObject("Title");
                titleObj.transform.SetParent(panel.transform, false);
            }
            RectTransform titleRt = titleObj.GetComponent<RectTransform>();
            if (titleRt == null) titleRt = titleObj.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -8f);
            titleRt.sizeDelta = new Vector2(0f, 24f);

            Text titleText = titleObj.GetComponent<Text>();
            if (titleText == null) titleText = titleObj.AddComponent<Text>();
            titleText.font = standardFont;
            titleText.text = "TILT / ROLL (HOLD)";
            titleText.fontSize = 13;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = new Color(0.85f, 0.85f, 0.9f);

            float centerOffset = 55f;
            float btnSize = 52f;

            _upButton = CreateArrowButton(panel.transform, "UpButton", new Vector2(0f, centerOffset), new Vector2(btnSize, btnSize), "▲", Vector2.up, standardFont);
            _downButton = CreateArrowButton(panel.transform, "DownButton", new Vector2(0f, -centerOffset), new Vector2(btnSize, btnSize), "▼", Vector2.down, standardFont);
            _leftButton = CreateArrowButton(panel.transform, "LeftButton", new Vector2(-centerOffset, 0f), new Vector2(btnSize, btnSize), "◀", Vector2.left, standardFont);
            _rightButton = CreateArrowButton(panel.transform, "RightButton", new Vector2(centerOffset, 0f), new Vector2(btnSize, btnSize), "▶", Vector2.right, standardFont);
        }

        private HoldButton CreateArrowButton(Transform parent, string name, Vector2 pos, Vector2 size, string label, Vector2 dir, Font font)
        {
            Transform existing = parent.Find(name);
            GameObject btnObj = (existing != null) ? existing.gameObject : new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            if (rt == null) rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = btnObj.GetComponent<Image>();
            if (img == null) img = btnObj.AddComponent<Image>();
            img.color = new Color(0.2f, 0.25f, 0.35f, 0.95f);

            HoldButton holdBtn = btnObj.GetComponent<HoldButton>();
            if (holdBtn == null) holdBtn = btnObj.AddComponent<HoldButton>();
            holdBtn.Initialize(_controller, dir);

            GameObject textObj = btnObj.transform.Find("Text")?.gameObject;
            if (textObj == null)
            {
                textObj = new GameObject("Text");
                textObj.transform.SetParent(btnObj.transform, false);
            }
            RectTransform textRt = textObj.GetComponent<RectTransform>();
            if (textRt == null) textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;

            Text txt = textObj.GetComponent<Text>();
            if (txt == null) txt = textObj.AddComponent<Text>();
            txt.font = font;
            txt.text = label;
            txt.fontSize = 24;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;

            return holdBtn;
        }
    }
}
