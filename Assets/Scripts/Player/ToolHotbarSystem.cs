using System;
using UnityEngine;
using UnityEngine.EventSystems;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Audio;

namespace BioFarme.Player
{
    /// <summary>
    /// Singleton 8-slot hotbar manager for BioFarme.
    /// Keeps slots 0..2 permanently assigned to CultivatorHoe, BioSprayer, and BoneHammer (keys 1..3),
    /// while slots 3..7 (keys 4..8) hold customizable Seeds and Structures assigned from the [B] Bio-Codex.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    public sealed class ToolHotbarSystem : MonoBehaviour
    {
        public const int HotbarSlotCount = 8;
        public const int FirstCustomSlotIndex = 3;

        private static ToolHotbarSystem _instance;
        private static bool _applicationQuitting;

        public static ToolHotbarSystem InstanceIfExists => _instance;

        public static ToolHotbarSystem Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<ToolHotbarSystem>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[ToolHotbarSystem]");
                        _instance = go.AddComponent<ToolHotbarSystem>();
                    }
                }
                return _instance;
            }
        }

        private readonly HotbarSlotData[] _slots = new HotbarSlotData[HotbarSlotCount];
        private int _selectedIndex;
        private int _lastSelectedCustomSlot = HotbarSlotCount - 1;

        public int SelectedIndex => _selectedIndex;
        public HotbarSlotData SelectedSlot => (_selectedIndex >= 0 && _selectedIndex < _slots.Length) ? _slots[_selectedIndex] : _slots[0];
        public HotbarSlotData[] Slots => _slots;

        private static readonly KeyCode[] AlphaSlotKeys =
        {
            KeyCode.Alpha1,
            KeyCode.Alpha2,
            KeyCode.Alpha3,
            KeyCode.Alpha4,
            KeyCode.Alpha5,
            KeyCode.Alpha6,
            KeyCode.Alpha7,
            KeyCode.Alpha8
        };

        private static readonly KeyCode[] KeypadSlotKeys =
        {
            KeyCode.Keypad1,
            KeyCode.Keypad2,
            KeyCode.Keypad3,
            KeyCode.Keypad4,
            KeyCode.Keypad5,
            KeyCode.Keypad6,
            KeyCode.Keypad7,
            KeyCode.Keypad8
        };

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            InitializeDefaultSlots(broadcastEvents: false);
        }

        private void OnEnable()
        {
            GameEvents.OnAssignHotbarSelection += HandleAssignHotbarSelection;
            GameEvents.OnNewGameSessionInitialized += HandleNewGameSessionInitialized;
        }

        private void Start()
        {
            BroadcastFullHotbarState();
        }

        private void OnDisable()
        {
            GameEvents.OnAssignHotbarSelection -= HandleAssignHotbarSelection;
            GameEvents.OnNewGameSessionInitialized -= HandleNewGameSessionInitialized;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private void Update()
        {
            if (GameManager.InstanceIfExists != null && !GameManager.InstanceIfExists.IsGameplayInteractive)
            {
                return;
            }

            // 1. Direct number keys 1..8
            for (int i = 0; i < HotbarSlotCount; i++)
            {
                if (Input.GetKeyDown(AlphaSlotKeys[i]) || Input.GetKeyDown(KeypadSlotKeys[i]))
                {
                    SelectSlot(i, playSound: true);
                    return;
                }
            }

            // 2. Mouse ScrollWheel cycling when LeftControl/RightControl is NOT held (Ctrl+Scroll is camera zoom)
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrlHeld)
            {
                bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (!pointerOverUi)
                {
                    float scroll = Input.mouseScrollDelta.y;
                    if (Mathf.Abs(scroll) < 0.001f)
                    {
                        scroll = Input.GetAxisRaw("Mouse ScrollWheel") * 10f;
                    }

                    if (scroll > 0.05f)
                    {
                        int prev = (_selectedIndex - 1 + HotbarSlotCount) % HotbarSlotCount;
                        SelectSlot(prev, playSound: true);
                    }
                    else if (scroll < -0.05f)
                    {
                        int next = (_selectedIndex + 1) % HotbarSlotCount;
                        SelectSlot(next, playSound: true);
                    }
                }
            }
        }

        public void InitializeDefaultSlots(bool broadcastEvents = true)
        {
            HotbarSlotData[] defaultCopy = BalanceDatabase.GetDefaultHotbarCopy();

            // Guarantee slots 0..2 are the 3 core tools
            _slots[0] = BalanceDatabase.CreateToolSlot(0, "1", ToolType.CultivatorHoe);
            _slots[1] = BalanceDatabase.CreateToolSlot(1, "2", ToolType.BioSprayer);
            _slots[2] = BalanceDatabase.CreateToolSlot(2, "3", ToolType.BoneHammer);

            for (int i = FirstCustomSlotIndex; i < HotbarSlotCount; i++)
            {
                if (defaultCopy != null && i < defaultCopy.Length && defaultCopy[i] != null)
                {
                    _slots[i] = CloneAndNormalizeSlot(defaultCopy[i], i);
                }
                else
                {
                    _slots[i] = BalanceDatabase.CreateSeedSlot(i, (i + 1).ToString(), CropType.Sporecap);
                }
            }

            _selectedIndex = 0;
            _lastSelectedCustomSlot = HotbarSlotCount - 1;

            if (broadcastEvents)
            {
                BroadcastFullHotbarState();
            }
        }

        public void SelectSlot(int index, bool playSound = true)
        {
            int clamped = Mathf.Clamp(index, 0, HotbarSlotCount - 1);
            bool changed = _selectedIndex != clamped;
            _selectedIndex = clamped;

            if (_selectedIndex >= FirstCustomSlotIndex)
            {
                _lastSelectedCustomSlot = _selectedIndex;
            }

            if (changed && playSound && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxUiClick, 0.55f, 0.04f);
            }

            GameEvents.RaiseHotbarSlotSelected(_selectedIndex, SelectedSlot);
        }

        private void HandleNewGameSessionInitialized()
        {
            InitializeDefaultSlots(broadcastEvents: true);
        }

        private void HandleAssignHotbarSelection(HotbarSlotData requested)
        {
            if (requested == null || requested.IsEmpty) return;

            // If a core Tool was somehow selected from UI, simply focus its permanent slot 0..2
            if (requested.category == HotbarItemCategory.Tool)
            {
                for (int i = 0; i < FirstCustomSlotIndex; i++)
                {
                    if (_slots[i] != null && _slots[i].toolType == requested.toolType)
                    {
                        SelectSlot(i, playSound: true);
                        return;
                    }
                }
                return;
            }

            // If this exact Seed or Structure is already on the hotbar AND the player currently has a tool slot (0..2) active,
            // check whether we can jump straight to it instead of creating a duplicate entry
            if (_selectedIndex < FirstCustomSlotIndex)
            {
                int existingIdx = FindExistingCustomSlot(requested);
                if (existingIdx >= FirstCustomSlotIndex)
                {
                    SelectSlot(existingIdx, playSound: true);
                    return;
                }
            }

            // Choose target custom slot in 3..7 (never overwriting 0=Hoe, 1=Sprayer, 2=Hammer)
            int targetSlot;
            if (_selectedIndex >= FirstCustomSlotIndex && _selectedIndex < HotbarSlotCount)
            {
                targetSlot = _selectedIndex;
            }
            else
            {
                int emptyIdx = FindFirstEmptyCustomSlot();
                targetSlot = emptyIdx >= FirstCustomSlotIndex
                    ? emptyIdx
                    : Mathf.Clamp(_lastSelectedCustomSlot, FirstCustomSlotIndex, HotbarSlotCount - 1);
            }

            // If another custom slot already holds this exact item and we're placing it into targetSlot, keep hotbar tidy
            _slots[targetSlot] = CloneAndNormalizeSlot(requested, targetSlot);
            _selectedIndex = targetSlot;
            _lastSelectedCustomSlot = targetSlot;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxUiClick, 0.7f, 0.03f);
            }

            BroadcastFullHotbarState();
        }

        private int FindExistingCustomSlot(HotbarSlotData candidate)
        {
            for (int i = FirstCustomSlotIndex; i < HotbarSlotCount; i++)
            {
                HotbarSlotData s = _slots[i];
                if (s == null || s.category != candidate.category) continue;

                if (candidate.category == HotbarItemCategory.Seed && s.cropType == candidate.cropType)
                {
                    return i;
                }
                if (candidate.category == HotbarItemCategory.Structure && s.structureType == candidate.structureType)
                {
                    return i;
                }
            }
            return -1;
        }

        private int FindFirstEmptyCustomSlot()
        {
            for (int i = FirstCustomSlotIndex; i < HotbarSlotCount; i++)
            {
                if (_slots[i] == null || _slots[i].IsEmpty)
                {
                    return i;
                }
            }
            return -1;
        }

        private static HotbarSlotData CloneAndNormalizeSlot(HotbarSlotData src, int slotIndex)
        {
            string keyLabel = (slotIndex + 1).ToString();

            if (src.category == HotbarItemCategory.Seed)
            {
                HotbarSlotData canonical = BalanceDatabase.CreateSeedSlot(slotIndex, keyLabel, src.cropType);
                if (!string.IsNullOrEmpty(src.displayName)) canonical.displayName = src.displayName;
                if (!string.IsNullOrEmpty(src.description)) canonical.description = src.description;
                if (!string.IsNullOrEmpty(src.iconKey)) canonical.iconKey = src.iconKey;
                if (src.costs != null && src.costs.Length > 0) canonical.costs = src.costs;
                return canonical;
            }

            if (src.category == HotbarItemCategory.Structure)
            {
                HotbarSlotData canonical = BalanceDatabase.CreateStructureSlot(slotIndex, keyLabel, src.structureType);
                if (!string.IsNullOrEmpty(src.displayName)) canonical.displayName = src.displayName;
                if (!string.IsNullOrEmpty(src.description)) canonical.description = src.description;
                if (!string.IsNullOrEmpty(src.iconKey)) canonical.iconKey = src.iconKey;
                if (src.costs != null && src.costs.Length > 0) canonical.costs = src.costs;
                return canonical;
            }

            return BalanceDatabase.CreateToolSlot(slotIndex, keyLabel, src.toolType);
        }

        public void BroadcastFullHotbarState()
        {
            GameEvents.RaiseHotbarLoadoutChanged(_slots);
            GameEvents.RaiseHotbarSlotSelected(_selectedIndex, SelectedSlot);
        }
    }
}
