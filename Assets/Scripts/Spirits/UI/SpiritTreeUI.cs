using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Spellbreakers.Spirits
{
    /// <summary>
    /// The spirit tree window: a Classic WoW shaped pane with one tab per element.
    ///
    /// It is built entirely in code rather than from a prefab, because the project has no UI prefab yet and
    /// the point of a first pass is that opening the tree works in every scene without any wiring. The
    /// layout is deliberately the familiar one - a tab per tree, tiers top to bottom, a detail pane on the
    /// right - so the shape is legible before the numbers behind it are.
    ///
    /// The window owns no rules. It asks <see cref="SpiritProgression"/> what is legal and draws the
    /// answer, which is why an illegal click explains itself instead of silently doing nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpiritTreeUI : MonoBehaviour
    {
        private const float WindowWidth = 1040f;
        private const float WindowHeight = 720f;
        private const float NodeSize = 84f;
        private const float RowHeight = 118f;
        private const float ColumnWidth = 210f;

        private static readonly Color BackdropColor = new Color(0.02f, 0.02f, 0.03f, 0.72f);
        private static readonly Color WindowColor = new Color(0.10f, 0.09f, 0.11f, 0.98f);
        private static readonly Color BorderColor = new Color(0.30f, 0.26f, 0.22f, 1f);
        private static readonly Color PanelColor = new Color(0.06f, 0.06f, 0.08f, 0.85f);
        private static readonly Color TabIdleColor = new Color(0.16f, 0.15f, 0.17f, 1f);
        private static readonly Color TextColor = new Color(0.90f, 0.88f, 0.84f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.60f, 0.57f, 1f);
        private static readonly Color LockedColor = new Color(0.20f, 0.20f, 0.22f, 1f);
        private static readonly Color CompleteBorderColor = new Color(0.95f, 0.82f, 0.45f, 1f);
        private static readonly Color ReadyColor = new Color(0.65f, 0.85f, 0.60f, 1f);
        private static readonly Color BlockedColor = new Color(0.85f, 0.65f, 0.45f, 1f);

        private static readonly Color FireColor = new Color(0.86f, 0.31f, 0.15f, 1f);
        private static readonly Color WindColor = new Color(0.44f, 0.80f, 0.47f, 1f);
        private static readonly Color EarthColor = new Color(0.73f, 0.55f, 0.26f, 1f);
        private static readonly Color WaterColor = new Color(0.30f, 0.60f, 0.92f, 1f);

        private sealed class NodeView
        {
            public SpiritTalent Talent;
            public Image Background;
            public Image Border;
            public TextMeshProUGUI RankLabel;
            public TextMeshProUGUI NameLabel;
        }

        private readonly List<NodeView> _nodes = new List<NodeView>();
        private readonly Dictionary<SpiritElement, Button> _tabButtons = new Dictionary<SpiritElement, Button>();
        private readonly Dictionary<SpiritElement, RectTransform> _tabContainers = new Dictionary<SpiritElement, RectTransform>();

        private SpiritProgression _progression;

        private RectTransform _root;
        private RectTransform _window;
        private RectTransform _treeArea;
        private TextMeshProUGUI _pointsLabel;
        private TextMeshProUGUI _footerHint;
        private TextMeshProUGUI _detailName;
        private TextMeshProUGUI _detailRank;
        private TextMeshProUGUI _detailDescription;
        private TextMeshProUGUI _detailStatus;
        private Button _learnButton;
        private TextMeshProUGUI _learnLabel;
        private Button _refundButton;
        private TextMeshProUGUI _refundLabel;

        private SpiritElement _selectedElement = SpiritElement.Fire;
        private string _selectedTalentId;

        private bool _built;

        /// <summary>Whether the window is currently on screen.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Set by the controller. Off means spending points does not touch the save file.</summary>
        public bool PersistenceEnabled { get; set; } = true;

        /// <summary>Builds the window against a progression and leaves it closed. Safe to call more than once.</summary>
        public void Build(SpiritProgression progression)
        {
            if (progression == null)
            {
                Debug.LogError("[Spirits] SpiritTreeUI needs a progression to draw.");
                return;
            }

            if (_built)
            {
                Bind(progression);
                return;
            }

            _progression = progression;
            EnsureEventSystem();
            BuildHierarchy();
            BuildTrees();

            _built = true;
            Bind(progression);
            ShowTab(SpiritElement.Fire);
            Hide();
        }

        private void Bind(SpiritProgression progression)
        {
            if (_progression != null)
                _progression.Changed -= Refresh;

            _progression = progression;

            if (_progression != null)
                _progression.Changed += Refresh;

            Refresh();
        }

        private void OnDestroy()
        {
            if (_progression != null)
                _progression.Changed -= Refresh;
        }

        public void Toggle()
        {
            if (IsOpen)
                Hide();
            else
                Show();
        }

        public void Show()
        {
            if (!_built)
                return;

            IsOpen = true;
            _root.gameObject.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            if (!_built)
                return;

            IsOpen = false;
            _root.gameObject.SetActive(false);
        }

        // --- Construction ---------------------------------------------------------

        private void BuildHierarchy()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            if (GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1600f, 900f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            _root = CreateRect("Root", transform);
            Stretch(_root, 0f);

            var backdrop = AddImage(_root, "Backdrop", BackdropColor);
            Stretch(backdrop.rectTransform, 0f);
            backdrop.raycastTarget = true;

            _window = CreateRect("Window", _root);
            CentreInParent(_window, WindowWidth, WindowHeight);

            var border = AddImage(_window, "Border", BorderColor);
            Stretch(border.rectTransform, -2f);

            var body = AddImage(_window, "Body", WindowColor);
            Stretch(body.rectTransform, 0f);
            body.raycastTarget = true;

            BuildHeader(body.transform);
            BuildTabs(body.transform);

            _treeArea = CreateRect("TreeArea", body.transform);
            Place(_treeArea, 28f, 130f, 640f, 540f);

            var detail = AddImage(body.transform, "Detail", PanelColor);
            Place(detail.rectTransform, 692f, 130f, 320f, 540f);
            BuildDetailPanel(detail.rectTransform);

            BuildFooter(body.transform);
        }

        private void BuildHeader(Transform parent)
        {
            var header = CreateRect("Header", parent);
            Place(header, 0f, 0f, WindowWidth, 66f);

            var title = AddText(header, "Title", "Spirits", 32f, TextColor, TextAlignmentOptions.Left);
            Place(title.rectTransform, 28f, 12f, 560f, 40f);

            _pointsLabel = AddText(header, "Points", string.Empty, 20f, TextColor, TextAlignmentOptions.Right);
            Place(_pointsLabel.rectTransform, WindowWidth - 320f, 18f, 220f, 30f);

            var close = AddButton(header, "Close", "✕", TabIdleColor, TextColor, 18f);
            Place(close.GetComponent<RectTransform>(), WindowWidth - 76f, 14f, 44f, 36f);
            close.onClick.AddListener(Hide);
        }

        private void BuildTabs(Transform parent)
        {
            var strip = CreateRect("Tabs", parent);
            Place(strip, 28f, 76f, WindowWidth - 56f, 40f);

            for (var i = 0; i < SpiritElements.All.Length; i++)
            {
                var element = SpiritElements.All[i];
                var tab = AddButton(
                    strip,
                    $"{element}Tab",
                    SpiritElements.ToDisplayName(element),
                    TabIdleColor,
                    TextColor,
                    17f);

                Place(tab.GetComponent<RectTransform>(), i * ColumnWidth, 0f, 200f, 38f);

                var captured = element;
                tab.onClick.AddListener(() => ShowTab(captured));
                _tabButtons[element] = tab;
            }
        }

        private void BuildDetailPanel(Transform parent)
        {
            _detailName = AddText(parent, "Name", string.Empty, 22f, TextColor, TextAlignmentOptions.Left);
            Place(_detailName.rectTransform, 20f, 18f, 280f, 30f);

            _detailRank = AddText(parent, "Rank", string.Empty, 16f, MutedColor, TextAlignmentOptions.Left);
            Place(_detailRank.rectTransform, 20f, 50f, 280f, 24f);

            _detailDescription = AddText(parent, "Description", string.Empty, 15f, MutedColor, TextAlignmentOptions.TopLeft);
            Place(_detailDescription.rectTransform, 20f, 84f, 280f, 330f);

            _detailStatus = AddText(parent, "Status", string.Empty, 14f, MutedColor, TextAlignmentOptions.TopLeft);
            Place(_detailStatus.rectTransform, 20f, 420f, 280f, 52f);

            _learnButton = AddButton(parent, "Learn", "Learn", TabIdleColor, TextColor, 16f);
            Place(_learnButton.GetComponent<RectTransform>(), 20f, 484f, 132f, 38f);
            _learnLabel = _learnButton.GetComponentInChildren<TextMeshProUGUI>();
            _learnButton.onClick.AddListener(LearnSelected);

            _refundButton = AddButton(parent, "Refund", "Refund", TabIdleColor, TextColor, 16f);
            Place(_refundButton.GetComponent<RectTransform>(), 168f, 484f, 132f, 38f);
            _refundLabel = _refundButton.GetComponentInChildren<TextMeshProUGUI>();
            _refundButton.onClick.AddListener(RefundSelected);
        }

        private void BuildFooter(Transform parent)
        {
            var footer = CreateRect("Footer", parent);
            Place(footer, 28f, 678f, WindowWidth - 56f, 34f);

            _footerHint = AddText(footer, "Hint", string.Empty, 15f, MutedColor, TextAlignmentOptions.Left);
            Place(_footerHint.rectTransform, 0f, 6f, 720f, 24f);

            var resetAll = AddButton(footer, "ResetAll", "Reset all trees", TabIdleColor, TextColor, 15f);
            var resetRect = resetAll.GetComponent<RectTransform>();
            resetRect.anchorMin = new Vector2(1f, 1f);
            resetRect.anchorMax = new Vector2(1f, 1f);
            resetRect.pivot = new Vector2(1f, 1f);
            resetRect.anchoredPosition = Vector2.zero;
            resetRect.sizeDelta = new Vector2(170f, 30f);
            resetAll.onClick.AddListener(ResetAll);
        }

        /// <summary>
        /// Lays out every tree up front and then shows one container at a time. Building all four costs a
        /// couple of dozen objects once and avoids destroying and recreating the tree on every tab press.
        /// </summary>
        private void BuildTrees()
        {
            foreach (var tree in _progression.Trees)
            {
                var container = CreateRect($"{tree.Element}Tree", _treeArea);
                Stretch(container, 0f);
                _tabContainers[tree.Element] = container;

                for (var tier = 1; tier <= tree.MaxTier; tier++)
                {
                    var row = CreateRect($"Tier{tier}", container);
                    Place(row, 0f, (tier - 1) * RowHeight, 640f, RowHeight);

                    var required = SpiritTree.PointsRequiredForTier(tier);
                    if (required > 0)
                    {
                        var gate = AddText(row, "Gate", $"{required} pts", 12f, MutedColor, TextAlignmentOptions.Right);
                        Place(gate.rectTransform, 520f, 34f, 110f, 20f);
                    }

                    foreach (var talent in tree.Talents)
                    {
                        if (talent.Tier != tier)
                            continue;

                        _nodes.Add(BuildNode(row, talent, talent.Column * ColumnWidth, 0f));
                    }
                }
            }
        }

        private NodeView BuildNode(Transform parent, SpiritTalent talent, float x, float y)
        {
            var view = new NodeView { Talent = talent };

            var slot = CreateRect($"Node_{talent.Id}", parent);
            Place(slot, x, y, NodeSize, NodeSize + 34f);

            view.Border = AddImage(slot, "Border", BorderColor);
            Place(view.Border.rectTransform, 0f, 0f, NodeSize, NodeSize);
            view.Border.raycastTarget = true;

            view.Background = AddImage(view.Border.transform, "Face", FireColor);
            Place(view.Background.rectTransform, 3f, 3f, NodeSize - 6f, NodeSize - 6f);

            var button = view.Border.gameObject.AddComponent<Button>();
            button.targetGraphic = view.Border;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 1f);
            button.colors = colors;

            var captured = talent;
            button.onClick.AddListener(() => OnNodeClicked(captured));

            view.RankLabel = AddText(view.Border.transform, "Rank", string.Empty, 18f, TextColor, TextAlignmentOptions.Center);
            Stretch(view.RankLabel.rectTransform, 4f);

            view.NameLabel = AddText(slot, "Name", talent.Name, 12f, TextColor, TextAlignmentOptions.Top);
            Place(view.NameLabel.rectTransform, -10f, NodeSize + 2f, NodeSize + 20f, 32f);

            return view;
        }

        // --- Interaction ----------------------------------------------------------

        private void ShowTab(SpiritElement element)
        {
            _selectedElement = element;

            foreach (var pair in _tabContainers)
                pair.Value.gameObject.SetActive(pair.Key == element);

            // A selection from another tree has nothing to show here.
            if (_progression != null && _progression.TreeOf(_selectedTalentId)?.Element != element)
                _selectedTalentId = null;

            Refresh();
        }

        private void OnNodeClicked(SpiritTalent talent)
        {
            _selectedTalentId = talent.Id;

            // Clicking a node buys a rank when it is legal, exactly as the reference tree window does; the
            // detail pane still explains a refusal.
            if (_progression.RankUp(talent.Id))
                Persist();

            Refresh();
        }

        private void LearnSelected()
        {
            if (string.IsNullOrEmpty(_selectedTalentId))
                return;

            if (_progression.RankUp(_selectedTalentId))
                Persist();

            Refresh();
        }

        private void RefundSelected()
        {
            if (string.IsNullOrEmpty(_selectedTalentId))
                return;

            if (_progression.Refund(_selectedTalentId))
                Persist();

            Refresh();
        }

        private void ResetAll()
        {
            if (_progression.ResetAll() > 0)
                Persist();

            Refresh();
        }

        private void Persist()
        {
            if (!PersistenceEnabled)
                return;

            SpiritProgressStore.Save(_progression);
        }

        // --- Drawing --------------------------------------------------------------

        private void Refresh()
        {
            if (!_built || _progression == null)
                return;

            _pointsLabel.text = $"Spirit points: {_progression.AvailablePoints} / {_progression.TotalPoints}";

            foreach (var pair in _tabButtons)
            {
                var selected = pair.Key == _selectedElement;
                pair.Value.image.color = selected ? ElementColor(pair.Key) : TabIdleColor;

                var label = pair.Value.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                    label.color = selected ? new Color(0.06f, 0.06f, 0.06f, 1f) : TextColor;
            }

            foreach (var node in _nodes)
                DrawNode(node);

            DrawDetail();

            _footerHint.text =
                $"Click a node to learn it · Spent {_progression.SpentPoints} · " +
                "Boon effects are not wired up yet";
        }

        private void DrawNode(NodeView node)
        {
            var talent = node.Talent;
            var state = _progression.StateOf(talent.Id);
            var rank = _progression.RankOf(talent.Id);
            var elementColor = ElementColor(talent.Element);

            switch (state)
            {
                case SpiritTalentState.Maxed:
                    node.Background.color = elementColor;
                    node.RankLabel.color = new Color(0.05f, 0.05f, 0.05f, 1f);
                    node.Border.color = CompleteBorderColor;
                    break;

                case SpiritTalentState.Available:
                    node.Background.color = new Color(
                        elementColor.r * 0.78f,
                        elementColor.g * 0.78f,
                        elementColor.b * 0.78f,
                        1f);
                    node.RankLabel.color = TextColor;
                    node.Border.color = talent.Id == _selectedTalentId ? Color.white : BorderColor;
                    break;

                default:
                    node.Background.color = LockedColor;
                    node.RankLabel.color = MutedColor;
                    node.Border.color = talent.Id == _selectedTalentId ? Color.white : BorderColor;
                    break;
            }

            node.NameLabel.color = state == SpiritTalentState.Locked ? MutedColor : TextColor;
            node.RankLabel.text = talent.MaxRank > 1
                ? $"{rank}/{talent.MaxRank}"
                : (rank > 0 ? "✓" : "0/1");
        }

        private void DrawDetail()
        {
            var talent = _progression.TreeOf(_selectedTalentId)?.Find(_selectedTalentId);

            if (talent == null)
            {
                _detailName.text = "Select a talent";
                _detailName.color = TextColor;
                _detailRank.text = string.Empty;
                _detailDescription.text =
                    "Choose a node to read what it does. Points can go into any tree, in any mixture - the " +
                    "Spirits do not demand a single path.";
                _detailStatus.text = string.Empty;
                _learnLabel.text = "Learn";
                _refundLabel.text = "Refund";
                _learnButton.interactable = false;
                _refundButton.interactable = false;
                return;
            }

            var rank = _progression.RankOf(talent.Id);
            var elementName = SpiritElements.ToDisplayName(talent.Element);

            _detailName.text = talent.Name;
            _detailName.color = ElementColor(talent.Element);
            _detailRank.text = talent.MaxRank > 1
                ? $"Rank {rank} / {talent.MaxRank} · {elementName}"
                : $"{(rank > 0 ? "Learned" : "Not learned")} · {elementName}";

            _detailDescription.text = talent.Description;

            var canLearn = _progression.CanRankUp(talent.Id, out var reason);
            _detailStatus.text = canLearn ? "Ready to learn." : reason;
            _detailStatus.color = canLearn ? ReadyColor : BlockedColor;

            _learnButton.interactable = canLearn;
            _refundButton.interactable = rank > 0;
            _learnLabel.text = canLearn ? $"Learn {talent.CostPerRank}" : "Learn";
            _refundLabel.text = rank > 0 ? $"Refund {talent.CostPerRank}" : "Refund";
        }

        private static Color ElementColor(SpiritElement element)
        {
            switch (element)
            {
                case SpiritElement.Wind: return WindColor;
                case SpiritElement.Earth: return EarthColor;
                case SpiritElement.Water: return WaterColor;
                default: return FireColor;
            }
        }

        // --- Helpers --------------------------------------------------------------

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            if (FindAnyObjectByType<EventSystem>() != null)
                return;

            var go = new GameObject("SpiritTree EventSystem");
            go.AddComponent<EventSystem>();

            // Everything else in this project runs through the new Input System, so the legacy stand-alone
            // module would never fire.
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image AddImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI AddText(
            Transform parent,
            string name,
            string text,
            float size,
            Color color,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;

            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;

            return label;
        }

        private static Button AddButton(
            Transform parent,
            string name,
            string text,
            Color background,
            Color textColor,
            float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = background;
            image.raycastTarget = true;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var label = AddText(go.transform, "Label", text, fontSize, textColor, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 0f);

            return button;
        }

        /// <summary>Places a rect with top-left anchoring so y grows downward, like a document.</summary>
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void CentreInParent(RectTransform rect, float width, float height)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
