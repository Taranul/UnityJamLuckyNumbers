using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Builds and manages the Debug Canvas UI with four directional arrow buttons (↑, ↓, ←, →).
    /// Handles press-and-hold interaction to control icosahedron rotation.
    /// </summary>
    public class DebugRotationUI : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private IcosahedronController controller;

        [Header("Button References (Optional if pre-constructed)")]
        [SerializeField] private HoldButton upButton;
        [SerializeField] private HoldButton downButton;
        [SerializeField] private HoldButton leftButton;
        [SerializeField] private HoldButton rightButton;

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<IcosahedronController>();
            }

            EnsureEventSystem();

            if (upButton == null || downButton == null || leftButton == null || rightButton == null)
            {
                BuildUI();
            }
        }

        private void EnsureEventSystem()
        {
            EventSystem es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                es = esObj.AddComponent<EventSystem>();
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
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // Clean previous children if any
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                if (Application.isPlaying)
                {
                    Destroy(transform.GetChild(i).gameObject);
                }
                else
                {
                    DestroyImmediate(transform.GetChild(i).gameObject);
                }
            }

            // Panel container in bottom-right corner
            GameObject panel = new GameObject("ControlPadPanel");
            panel.transform.SetParent(transform, false);
            RectTransform panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(1f, 0f);
            panelRect.anchoredPosition = new Vector2(-40f, 40f);
            panelRect.sizeDelta = new Vector2(260f, 260f);

            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.12f, 0.18f, 0.75f);

            // Title label
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(panel.transform, false);
            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -8f);
            titleRect.sizeDelta = new Vector2(240f, 30f);

            Text titleText = titleObj.AddComponent<Text>();
            titleText.text = "ROTATION CONTROLS";
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.fontSize = 14;
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.color = new Color(0.9f, 0.9f, 0.9f, 1f);

            float btnSize = 60f;
            float centerOffsetY = -20f;

            // Up Button
            upButton = CreateArrowButton(panel.transform, "UpButton", "▲", new Vector2(0f, 65f + centerOffsetY), btnSize, new Vector2(0f, 1f));
            // Down Button
            downButton = CreateArrowButton(panel.transform, "DownButton", "▼", new Vector2(0f, -65f + centerOffsetY), btnSize, new Vector2(0f, -1f));
            // Left Button
            leftButton = CreateArrowButton(panel.transform, "LeftButton", "◀", new Vector2(-65f, centerOffsetY), btnSize, new Vector2(-1f, 0f));
            // Right Button
            rightButton = CreateArrowButton(panel.transform, "RightButton", "▶", new Vector2(65f, centerOffsetY), btnSize, new Vector2(1f, 0f));
        }

        private HoldButton CreateArrowButton(Transform parent, string name, string label, Vector2 position, float size, Vector2 direction)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);

            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(size, size);

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.25f, 0.35f, 0.55f, 0.9f);

            HoldButton holdBtn = btnObj.AddComponent<HoldButton>();
            holdBtn.Initialize(controller, direction);

            // Text label
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            Text txt = textObj.AddComponent<Text>();
            txt.text = label;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontSize = 24;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.color = Color.white;

            return holdBtn;
        }
    }
}
