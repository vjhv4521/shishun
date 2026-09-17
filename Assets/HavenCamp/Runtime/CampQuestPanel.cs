using System;
using System.Collections;
using System.Text;
using Haven.Framework.Bootstrap;
using Haven.Framework.CampQuests;
using Haven.Framework.Core;
using SurvivalEngine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Haven.Camp
{
    public sealed class CampQuestPanel : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset font;
        private ICampQuestService _service;
        private ICampNpcChatService _chatService;
        private IDisposable _subscription;
        private GameObject _dialog;
        private TMP_Text _heading;
        private TMP_Text _dialogue;
        private TMP_Text _need;
        private TMP_Text _reward;
        private TMP_Text _status;
        private TMP_Text _tracker;
        private TMP_Text _day;
        private TMP_InputField _input;
        private TMP_InputField _chatInput;
        private TMP_Text _chatLog;
        private TMP_Text _chatStatus;
        private RectTransform _chatContent;
        private ScrollRect _chatScroll;
        private GameObject _chatPage;
        private Button _chatSend;
        private Button _chatOpen;
        private string _renderedChat;
        private Image _needIcon;
        private Image _rewardIcon;
        private Button _ask;
        private Button _accept;
        private Button _deliver;
        private Button _abandon;
        private Button[] _preferences;
        private string _preference = "urgent";
        private string _session;
        private bool _ownsPause;
        private float _nextRefresh;
        private readonly Color _ink = new Color(0.91f, 0.91f, 0.82f);
        private readonly Color _gold = new Color(0.78f, 0.61f, 0.32f);

        public bool IsOpen => _dialog && _dialog.activeSelf;
        public bool IsChatOpen => IsOpen && _chatPage && _chatPage.activeSelf;
        public void Configure(TMP_FontAsset value) { font = value; }

        private void Awake() { CreateUi(); }

        private void Update()
        {
            if (_service == null)
            {
                var context = GameBootstrap.Instance?.Context;
                if (context != null && context.Services.TryResolve<ICampQuestService>(out _service))
                    _subscription = context.Events.Subscribe<CampQuestChanged>(change => _status.text = change.Message);
            }
            if (_chatService == null)
                GameBootstrap.Instance?.Context?.Services.TryResolve<ICampNpcChatService>(out _chatService);
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        public void Open()
        {
            if (IsOpen) return;
            _dialog.SetActive(true);
            _chatPage.SetActive(false);
            var game = TheGame.Get();
            _ownsPause = game && !game.IsPausedByScript();
            if (_ownsPause) game.PauseScripts();
            _session = _service?.GetView().World?.sessionId;
            _status.text = "物资交付后计入营地储备，不会自动建造或添加燃料。";
            Refresh();
        }

        public void Close()
        {
            if (_dialog) _dialog.SetActive(false);
            if (_ownsPause && TheGame.Get()) TheGame.Get().UnpauseScripts();
            _ownsPause = false;
            if (_input) _input.DeactivateInputField();
            if (_chatInput) _chatInput.DeactivateInputField();
        }

        private void Refresh()
        {
            if (_service == null)
            {
                _tracker.text = "营地物资委托\n正在准备任务系统…";
                _ask.interactable = false;
                return;
            }
            var view = _service.GetView();
            if (view.World == null) { if (IsOpen) Close(); return; }
            if (IsOpen && (!view.World.alive || view.World.distance > 3.2f || (_session != null && _session != view.World.sessionId))) Close();
            var record = view.Active ?? view.Offer;
            _day.text = $"HAVEN   /   第 {view.World.day} 天   {Mathf.FloorToInt(view.World.hour):00}:00";
            _heading.text = record?.definition.title ?? "营地物资委托";
            _dialogue.text = record?.dialogue ?? "荒野中的日子需要互相照应。告诉我你今天想做些什么，我来看看营地还缺哪些储备。";
            if (view.Busy) _dialogue.text = "管事正在查看营地储备…\n你可以关闭面板，稍后再来查看。";
            var definition = record?.definition;
            var held = definition == null ? 0 : definition.itemId == "wood" ? view.World.wood : view.World.rock;
            _need.text = definition == null ? "交付物资\n木材 / 石料" : $"交付{ItemName(definition.itemId)} ×{definition.quantity}\n随身持有 {held}";
            _reward.text = definition == null ? "营地回礼\n新鲜面包" : $"面包 ×{definition.rewardQuantity}\n交付时一次结算";
            _needIcon.sprite = ItemData.Get(definition?.itemId ?? "wood")?.icon;
            _rewardIcon.sprite = ItemData.Get("bread")?.icon;
            _tracker.text = view.Active == null
                ? $"营地物资委托\n点击出生点附近的营地管事\n今日完成 {view.CompletedToday}/2"
                : $"{view.Active.definition.title}\n{ItemName(view.Active.definition.itemId)} {view.Held}/{view.Active.definition.quantity}  ·  返回管事处交付\n今日完成 {view.CompletedToday}/2";
            _ask.gameObject.SetActive(record == null);
            _accept.gameObject.SetActive(view.Offer != null && view.Active == null);
            _deliver.gameObject.SetActive(view.Active != null);
            _abandon.gameObject.SetActive(record != null);
            var available = !view.Busy && string.IsNullOrEmpty(view.Error) && view.World.alive;
            _ask.interactable = available && view.CompletedToday < 2;
            _accept.interactable = available;
            _deliver.interactable = available && definition != null && held >= definition.quantity;
            _abandon.interactable = available;
            _input.interactable = available && record == null;
            _chatOpen.interactable = _chatService != null && view.World.alive;
            _chatSend.interactable = _chatService != null && !_chatService.Busy;
            _chatInput.interactable = _chatService != null && !_chatService.Busy;
            foreach (var button in _preferences) button.interactable = available && record == null;
            if (IsChatOpen) RenderChat();
            if (!string.IsNullOrEmpty(view.Error)) _status.text = view.Error;
            else if (view.CompletedToday >= 2 && record == null) _status.text = "今天的两项委托已经完成。谢谢你的帮助，明天再来吧。";
        }

        private void Ask()
        {
            if (_service != null) StartCoroutine(SafeCoroutine.Run(_service.Propose(_preference, _input.text, ShowResult),
                exception => _status.text = "本次询问未完成，请关闭面板后再试。"));
        }

        private void ShowResult(FrameworkResult result)
        {
            if (!result.Succeeded) _status.text = result.Error.Message;
            Refresh();
        }

        private void SelectPreference(string value)
        {
            _preference = value;
            var values = new[] { "easy", "urgent", "more" };
            for (var index = 0; index < _preferences.Length; index++)
                _preferences[index].GetComponent<Image>().color = values[index] == value ? new Color(0.34f, 0.40f, 0.24f) : new Color(0.19f, 0.23f, 0.18f);
        }

        public void OpenChat()
        {
            if (!IsOpen || _chatService == null) return;
            _chatPage.SetActive(true);
            _chatStatus.text = "只聊营地与当前处境；聊天不会直接改变背包或委托。";
            RenderChat();
        }

        public void ShowQuests()
        {
            if (!_chatPage) return;
            _chatInput.DeactivateInputField();
            _chatPage.SetActive(false);
            Refresh();
        }

        private void SendChat()
        {
            if (_chatService == null || _chatService.Busy || string.IsNullOrWhiteSpace(_chatInput.text)) return;
            var message = _chatInput.text;
            _chatStatus.text = "管事正在想…";
            StartCoroutine(SafeCoroutine.Run(_chatService.Send(message, result =>
            {
                if (result.Succeeded)
                {
                    _chatInput.text = string.Empty;
                    _chatStatus.text = result.Value.source == "deepseek" ? "在线回复" : "本地回复（当前无法连接 AI）";
                    RenderChat();
                }
                else _chatStatus.text = result.Error.Message;
            }), exception => _chatStatus.text = "交谈未完成，请稍后再试。"));
        }

        private void RenderChat()
        {
            if (_chatService == null) return;
            var history = _chatService.GetHistory();
            var transcript = new StringBuilder();
            if (history.Length == 0) transcript.Append("管事：营地里有什么想问的？我知道眼下的物资和委托。\n");
            foreach (var turn in history)
            {
                transcript.Append(turn.role == "player" ? "你：" : "管事：");
                transcript.Append(turn.text).Append("\n\n");
            }
            var rendered = transcript.ToString();
            if (rendered == _renderedChat) return;
            _renderedChat = rendered;
            _chatLog.text = rendered;
            _chatLog.ForceMeshUpdate();
            var height = Mathf.Max(326, _chatLog.preferredHeight + 22);
            _chatContent.sizeDelta = new Vector2(0, height);
            _chatLog.rectTransform.sizeDelta = new Vector2(610, height - 16);
            Canvas.ForceUpdateCanvases();
            _chatScroll.verticalNormalizedPosition = 0;
        }

        private void CreateUi()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            var tracker = Box(transform, "Quest tracker", new Vector2(330, 122), new Color(0.055f, 0.08f, 0.06f, 0.91f));
            tracker.anchorMin = tracker.anchorMax = new Vector2(1, 1);
            tracker.pivot = new Vector2(1, 1);
            tracker.anchoredPosition = new Vector2(-20, -20);
            tracker.GetComponent<Image>().raycastTarget = false;
            _tracker = Label(tracker, "Tracker text", 20, 15, 290, 96, 18, _ink);

            var overlay = Box(transform, "Camp modal input shield", Vector2.zero, new Color(0, 0, 0, 0.30f));
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            _dialog = overlay.gameObject;
            var dialog = Box(overlay, "Camp quest panel", new Vector2(720, 590), new Color(0.075f, 0.10f, 0.075f, 0.99f));
            dialog.anchorMin = dialog.anchorMax = new Vector2(0.5f, 0.5f);
            dialog.pivot = new Vector2(0.5f, 0.5f);
            dialog.anchoredPosition = Vector2.zero;
            _day = Label(dialog, "Day", 30, 20, 590, 25, 15, _gold);
            Label(dialog, "Steward", 30, 55, 580, 44, 30, _ink).text = "营地管事";
            _chatOpen = MakeButton(dialog, "聊聊营地", 475, 23, 139, 34, OpenChat);
            MakeButton(dialog, "关闭", 626, 23, 70, 34, Close);
            _heading = Label(dialog, "Quest title", 30, 108, 650, 32, 23, _gold);
            _dialogue = Label(dialog, "Dialogue", 30, 148, 650, 87, 20, _ink);
            _dialogue.enableWordWrapping = true;

            var materials = Box(dialog, "Material card", new Vector2(320, 78), new Color(0.13f, 0.17f, 0.13f));
            Place(materials, 30, 243, 320, 78);
            _needIcon = Icon(materials, "Material icon", 14, 12);
            _need = Label(materials, "Material detail", 78, 12, 232, 62, 18, _ink);
            var rewards = Box(dialog, "Reward card", new Vector2(320, 78), new Color(0.13f, 0.17f, 0.13f));
            Place(rewards, 366, 243, 320, 78);
            _rewardIcon = Icon(rewards, "Reward icon", 14, 12);
            _reward = Label(rewards, "Reward detail", 78, 12, 232, 62, 18, _ink);

            _preferences = new[]
            {
                MakeButton(dialog, "轻松一些", 30, 337, 204, 37, () => SelectPreference("easy")),
                MakeButton(dialog, "营地最急需", 247, 337, 220, 37, () => SelectPreference("urgent")),
                MakeButton(dialog, "多搬一点", 480, 337, 206, 37, () => SelectPreference("more"))
            };
            SelectPreference("urgent");
            var inputRect = Box(dialog, "Preference input", new Vector2(656, 43), new Color(0.04f, 0.065f, 0.045f));
            Place(inputRect, 30, 389, 656, 43);
            _input = inputRect.gameObject.AddComponent<TMP_InputField>();
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(inputRect, false);
            Place(viewport, 12, 5, 632, 33);
            var inputText = Label(viewport, "Input text", 0, 0, 632, 33, 18, _ink);
            var placeholder = Label(viewport, "Placeholder", 0, 0, 632, 33, 18, new Color(0.5f, 0.57f, 0.47f));
            placeholder.text = "可选：例如“今天我想采些石头”（最多 120 字）";
            _input.textViewport = viewport;
            _input.textComponent = inputText;
            _input.placeholder = placeholder;
            _input.characterLimit = 120;
            _input.lineType = TMP_InputField.LineType.SingleLine;
            _input.caretColor = _ink;
            _input.customCaretColor = true;
            _status = Label(dialog, "Status", 30, 444, 656, 56, 16, new Color(0.70f, 0.76f, 0.65f));
            _ask = MakeButton(dialog, "询问营地需求", 30, 517, 320, 44, Ask);
            _accept = MakeButton(dialog, "接受这项委托", 30, 517, 320, 44, () => ShowResult(_service.Accept()));
            _deliver = MakeButton(dialog, "交付物资并领取口粮", 30, 517, 320, 44, () => ShowResult(_service.Deliver()));
            _abandon = MakeButton(dialog, "放弃 / 换一项", 366, 517, 320, 44, () => ShowResult(_service.Abandon()));
            _accept.gameObject.SetActive(false);
            _deliver.gameObject.SetActive(false);
            _abandon.gameObject.SetActive(false);

            var chat = Box(dialog, "Camp chat page", new Vector2(720, 590), new Color(0.075f, 0.10f, 0.075f, 1));
            Place(chat, 0, 0, 720, 590);
            _chatPage = chat.gameObject;
            Label(chat, "Chat heading", 30, 23, 430, 36, 28, _ink).text = "与营地管事交谈";
            MakeButton(chat, "询问委托", 475, 23, 139, 34, ShowQuests);
            MakeButton(chat, "关闭", 626, 23, 70, 34, Close);
            Label(chat, "Chat note", 30, 68, 650, 27, 15, _gold).text = "管事只提供建议；物资与任务仍由游戏规则结算。";
            var scrollBox = Box(chat, "Chat transcript", new Vector2(656, 330), new Color(0.045f, 0.07f, 0.05f));
            Place(scrollBox, 30, 104, 656, 330);
            _chatScroll = scrollBox.gameObject.AddComponent<ScrollRect>();
            _chatScroll.horizontal = false;
            _chatScroll.vertical = true;
            _chatScroll.movementType = ScrollRect.MovementType.Clamped;
            var scrollViewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask)).GetComponent<RectTransform>();
            scrollViewport.SetParent(scrollBox, false);
            Place(scrollViewport, 8, 7, 640, 316);
            scrollViewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            scrollViewport.GetComponent<Mask>().showMaskGraphic = false;
            _chatContent = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            _chatContent.SetParent(scrollViewport, false);
            _chatContent.anchorMin = new Vector2(0, 1);
            _chatContent.anchorMax = new Vector2(1, 1);
            _chatContent.pivot = new Vector2(0, 1);
            _chatContent.anchoredPosition = Vector2.zero;
            _chatContent.sizeDelta = new Vector2(0, 326);
            _chatLog = Label(_chatContent, "Chat text", 12, 8, 610, 310, 18, _ink);
            _chatLog.enableWordWrapping = true;
            _chatScroll.viewport = scrollViewport;
            _chatScroll.content = _chatContent;
            var chatInputBox = Box(chat, "Chat input", new Vector2(518, 44), new Color(0.04f, 0.065f, 0.045f));
            Place(chatInputBox, 30, 449, 518, 44);
            _chatInput = chatInputBox.gameObject.AddComponent<TMP_InputField>();
            var chatInputViewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            chatInputViewport.SetParent(chatInputBox, false);
            Place(chatInputViewport, 12, 5, 494, 34);
            var chatInputText = Label(chatInputViewport, "Input text", 0, 0, 494, 34, 18, _ink);
            var chatPlaceholder = Label(chatInputViewport, "Placeholder", 0, 0, 494, 34, 18, new Color(0.5f, 0.57f, 0.47f));
            chatPlaceholder.text = "问问营地、夜晚、眼下的委托…（120 字内）";
            _chatInput.textViewport = chatInputViewport;
            _chatInput.textComponent = chatInputText;
            _chatInput.placeholder = chatPlaceholder;
            _chatInput.characterLimit = 120;
            _chatInput.lineType = TMP_InputField.LineType.SingleLine;
            _chatInput.caretColor = _ink;
            _chatInput.customCaretColor = true;
            _chatSend = MakeButton(chat, "发送", 560, 449, 126, 44, SendChat);
            _chatStatus = Label(chat, "Chat status", 30, 505, 650, 52, 16, new Color(0.70f, 0.76f, 0.65f));
            _chatPage.SetActive(false);
            _dialog.SetActive(false);
        }

        private RectTransform Box(Transform parent, string name, Vector2 size, Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.GetComponent<Image>().color = color;
            return rect;
        }

        private TMP_Text Label(Transform parent, string name, float x, float y, float w, float h, float size, Color color)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            Place(label.rectTransform, x, y, w, h);
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.richText = false;
            label.raycastTarget = false;
            label.text = string.Empty;
            return label;
        }

        private Button MakeButton(Transform parent, string title, float x, float y, float w, float h, UnityEngine.Events.UnityAction clicked)
        {
            var rect = Box(parent, title, new Vector2(w, h), new Color(0.27f, 0.34f, 0.23f));
            Place(rect, x, y, w, h);
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(clicked);
            var text = Label(rect, "Text", 0, 0, w, h, 18, _ink);
            text.text = title;
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private Image Icon(Transform parent, string name, float x, float y)
        {
            var rect = Box(parent, name, new Vector2(52, 52), Color.white);
            Place(rect, x, y, 52, 52);
            rect.GetComponent<Image>().preserveAspect = true;
            return rect.GetComponent<Image>();
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private static string ItemName(string id) => id == "wood" ? "木材" : "石料";
        private void OnDestroy() { Close(); _subscription?.Dispose(); }
    }
}
