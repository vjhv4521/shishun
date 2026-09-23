using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haven.Framework.Bootstrap;
using Haven.Framework.Services;
using SurvivalEngine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Haven.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class NetworkSurvivalPlayerAdapter : MonoBehaviour, INetworkPlayerSimulation
    {
        private const string WorldSceneName = "WorldGenMap";

        [SerializeField] private MonoBehaviour[] _gameplayBehaviours = Array.Empty<MonoBehaviour>();

        private PlayerCharacter _character;
        private Rigidbody _rigidbody;
        private Collider[] _colliders;
        private Animator _animator;
        private ISurvivalCommandRouter _router;
        private SurvivalWorldNetworkBridge _worldBridge;
        private Coroutine _initializeRoutine;
        private bool _isServer;
        private bool _isOwner;
        private bool _initialized;
        private int _playerId = -1;

        public bool IsReady => _initialized && _worldBridge && _character;
        public bool IsLocalOwner => _isOwner;
        public bool IsServerAuthority => _isServer;
        public int PlayerId => _playerId;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _rigidbody = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _animator = GetComponentInChildren<Animator>(true);
            SetDormant();
        }

        public void ConfigureGameplayBehaviours(MonoBehaviour[] behaviours)
        {
            _gameplayBehaviours = behaviours ?? Array.Empty<MonoBehaviour>();
        }

        public void InitializeNetworkRole(bool isServer, bool isOwner, int playerId, ISurvivalCommandRouter router)
        {
            _isServer = isServer;
            _isOwner = isOwner;
            _playerId = playerId;
            _router = router;
            if (_initialized)
            {
                EnableForRole();
                if (_isOwner)
                    BindLocalPresentation();
                return;
            }
            if (_initializeRoutine == null)
                _initializeRoutine = StartCoroutine(InitializeWhenWorldReady());
        }

        public Vector3 CaptureWorldMovement()
        {
            if (!IsReady || !_isOwner || !_character.IsControlsEnabled() ||
                (TheGame.Get() && TheGame.Get().IsPaused()))
                return Vector3.zero;

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return Vector3.zero;
            var input = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            var camera = Camera.main;
            var forward = camera ? camera.transform.forward : Vector3.forward;
            var right = camera ? camera.transform.right : Vector3.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
            return Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
        }

        public void ApplyServerMovement(Vector3 movement)
        {
            if (!IsReady || !_isServer)
                return;
            _character.SetNetworkMoveInput(movement);
        }

        public SurvivalPublicPlayerSnapshot CapturePublicState()
        {
            var equipped = _character && _character.EquipData != null
                ? _character.EquipData.GetEquippedWeapon()?.item_id
                : string.Empty;
            return new SurvivalPublicPlayerSnapshot
            {
                PlayerId = _playerId,
                Position = transform.position,
                Rotation = transform.rotation,
                IsMoving = _character && _character.IsMoving(),
                IsBusy = _character && _character.IsBusy(),
                IsDead = _character && _character.IsDead(),
                Health = _character && _character.Attributes
                    ? _character.Attributes.GetAttributeValue(AttributeType.Health) : 0f,
                EquippedItemId = equipped ?? string.Empty
            };
        }

        public void ApplyPublicState(SurvivalPublicPlayerSnapshot state)
        {
            if (!_initialized || state.PlayerId != _playerId || _isServer)
                return;
            // NetworkTransform owns position and rotation interpolation; the public state only drives visuals.
            if (_animator)
            {
                _animator.enabled = true;
                SetAnimatorBool("Move", state.IsMoving);
                SetAnimatorBool("Death", state.IsDead);
            }
            if (!_isOwner)
                PlayerData.Get().GetPlayerCharacter(_playerId).attributes[AttributeType.Health] = state.Health;
            ApplyVisibleEquipment(state.EquippedItemId);
        }

        public bool ExecuteServerCommand(SurvivalCommand command, out string errorCode, out string message)
        {
            errorCode = GameplayErrorCodes.InvalidRequest;
            message = "无效的生存操作。";
            if (!IsReady || !_isServer)
                return false;
            return SurvivalCommandRouting.Execute(_character, command, out errorCode, out message);
        }

        public SurvivalSnapshot CaptureSnapshot(long revision)
        {
            return IsReady && _isServer
                ? _worldBridge.CaptureSnapshot(_playerId, revision)
                : SurvivalSnapshot.Empty;
        }

        public void ApplySnapshot(SurvivalSnapshot snapshot)
        {
            if (!IsReady || !_isOwner || _isServer || snapshot.LocalPlayerId != _playerId)
                return;
            _worldBridge.ApplySnapshot(snapshot);
        }

        public void ShutdownNetworkRole()
        {
            if (_initializeRoutine != null)
                StopCoroutine(_initializeRoutine);
            _initializeRoutine = null;
            if (_character)
            {
                _character.ClearNetworkMoveInput();
                _character.SetNetworkLocalInputOnly(false);
                _character.DisableControls();
                _character.DisableMovement();
            }
            _initialized = false;
            _router = null;
            SetDormant();
        }

        private IEnumerator InitializeWhenWorldReady()
        {
            // A network avatar is spawned in the lobby. Waiting for a guest must not consume
            // the world-initialization timeout before the host starts the game.
            while (!SurvivalMultiplayerRuntime.IsActive())
                yield return null;
            var deadline = Time.realtimeSinceStartup + 60f;
            Scene world = default;
            while (Time.realtimeSinceStartup < deadline)
            {
                world = SceneManager.GetSceneByName(WorldSceneName);
                if (world.IsValid() && world.isLoaded && PlayerData.Get() != null && TheGame.Get() != null)
                    break;
                yield return null;
            }

            if (!world.IsValid() || !world.isLoaded || PlayerData.Get() == null || !_character)
            {
                Debug.LogError($"[Haven] Network player {_playerId} could not initialize because {WorldSceneName} was not ready.");
                _initializeRoutine = null;
                yield break;
            }

            _character.player_id = Mathf.Max(0, _playerId);
            MoveToWorldSpawn(world);
            _worldBridge = SurvivalWorldNetworkBridge.GetOrCreate(world, _isServer);
            if (!_worldBridge)
            {
                Debug.LogError("[Haven] Multiplayer world has invalid interaction IDs; refusing to start the session.");
                var context = GameBootstrap.Instance?.Context;
                if (context != null && context.Services.TryResolve<INetworkService>(out var network))
                    network.Disconnect();
                _initializeRoutine = null;
                yield break;
            }
            EnableForRole();
            if (_isOwner)
                BindLocalPresentation();
            _worldBridge.Register(this);
            _initialized = true;
            _initializeRoutine = null;
        }

        private void MoveToWorldSpawn(Scene world)
        {
            if (gameObject.scene != world)
                SceneManager.MoveGameObjectToScene(gameObject, world);

            var spawn = Vector3.zero;
            foreach (var candidate in FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate == _character || candidate.gameObject.scene != world ||
                    candidate.GetComponent<NetworkSurvivalPlayerAdapter>())
                    continue;
                spawn = candidate.transform.position;
                candidate.gameObject.SetActive(false);
                break;
            }
            var offset = new Vector3((_playerId % 2) * 1.5f, 0f, (_playerId / 2) * 1.5f);
            if (_isServer)
                transform.position = spawn + offset;
        }

        private void EnableForRole()
        {
            if (_isServer)
            {
                foreach (var behaviour in _gameplayBehaviours)
                {
                    if (behaviour)
                        behaviour.enabled = true;
                }
                if (_rigidbody)
                {
                    _rigidbody.isKinematic = false;
                    _rigidbody.detectCollisions = true;
                }
                SetColliders(true);
                _character.move_enabled = true;
                _character.SetNetworkLocalInputOnly(false);
                _character.EnableMovement();
                if (_isOwner)
                    _character.EnableControls();
                else
                    _character.DisableControls();
                _character.SetNetworkMoveInput(Vector3.zero);
            }
            else
            {
                EnableBehaviour<PlayerCharacterInventory>();
                if (_isOwner)
                {
                    EnableBehaviour<PlayerCharacter>();
                    _character.move_enabled = true;
                    _character.SetNetworkLocalInputOnly(true);
                    _character.DisableMovement();
                    _character.EnableControls();
                }
                if (_rigidbody)
                {
                    _rigidbody.isKinematic = true;
                    _rigidbody.detectCollisions = false;
                }
                SetColliders(false);
            }

            if (_animator)
                _animator.enabled = true;
        }

        private void BindLocalPresentation()
        {
            var camera = TheCamera.Get();
            if (camera)
                camera.follow_target = gameObject;
        }

        private void SetDormant()
        {
            foreach (var behaviour in _gameplayBehaviours)
            {
                if (behaviour && behaviour != this)
                    behaviour.enabled = false;
            }
            if (_rigidbody)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.detectCollisions = false;
                _rigidbody.linearVelocity = Vector3.zero;
            }
            SetColliders(false);
            if (_animator)
                _animator.enabled = false;
        }

        private void EnableBehaviour<T>() where T : Behaviour
        {
            var behaviour = GetComponent<T>();
            if (behaviour)
                behaviour.enabled = true;
        }

        private void SetColliders(bool value)
        {
            foreach (var collider in _colliders ?? Array.Empty<Collider>())
            {
                if (collider)
                    collider.enabled = value;
            }
        }

        private void SetAnimatorBool(string parameter, bool value)
        {
            foreach (var item in _animator.parameters)
            {
                if (item.type == AnimatorControllerParameterType.Bool && item.name == parameter)
                {
                    _animator.SetBool(parameter, value);
                    break;
                }
            }
        }

        private void ApplyVisibleEquipment(string itemId)
        {
            if (!_character || _character.EquipData == null)
                return;
            var current = _character.EquipData.GetEquippedWeapon();
            if (string.Equals(current?.item_id, itemId, StringComparison.Ordinal))
                return;
            if (current != null)
                _character.EquipData.UnequipItem(_character.EquipData.GetEquippedWeaponSlot());
            var data = ItemData.Get(itemId);
            if (data && data.type == ItemType.Equipment)
                _character.EquipData.EquipItem(data.equip_slot, data.id, data.durability, "net-visible-" + _playerId);
        }
    }

    public static class SurvivalMultiplayerRuntime
    {
        public static bool IsActive()
        {
            var context = GameBootstrap.Instance?.Context;
            return context != null && context.Services.TryResolve<IRoomService>(out var room) &&
                   (room.Current.Phase == RoomPhase.Loading || room.Current.Phase == RoomPhase.InGame);
        }

        public static bool IsHostAuthority()
        {
            var context = GameBootstrap.Instance?.Context;
            return context != null && context.Services.TryResolve<INetworkHostService>(out var host) && host.IsHosting;
        }

        public static PlayerCharacter GetLocalPlayer()
        {
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<NetworkSurvivalPlayerAdapter>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                INetworkPlayerSimulation simulation = behaviour;
                if (simulation.IsLocalOwner && simulation.IsReady)
                    return behaviour.GetComponent<PlayerCharacter>();
            }
            return null;
        }

        public static ISurvivalCommandRouter GetRouter(GameObject source)
        {
            if (!source)
                return null;
            foreach (var behaviour in source.GetComponents<MonoBehaviour>())
            {
                if (behaviour is ISurvivalCommandRouter router)
                    return router;
            }
            return null;
        }
    }

    public static class SurvivalCommandRouting
    {
        public static SurvivalInventoryKind ToKind(PlayerCharacterInventory owner, InventoryData inventory)
        {
            if (!owner || inventory == null)
                return SurvivalInventoryKind.None;
            if (ReferenceEquals(inventory, owner.InventoryData))
                return SurvivalInventoryKind.Inventory;
            if (ReferenceEquals(inventory, owner.EquipData))
                return SurvivalInventoryKind.Equipment;
            if (ReferenceEquals(inventory, owner.BagData))
                return SurvivalInventoryKind.Bag;
            return SurvivalInventoryKind.None;
        }

        public static bool TrySubmit(PlayerCharacter character, SurvivalCommand command)
        {
            var router = character ? SurvivalMultiplayerRuntime.GetRouter(character.gameObject) : null;
            if (router == null)
                return false;
            if (router.ShouldRouteCommands)
                router.Submit(command);
            return router.BlockLocalCommands;
        }

        public static bool Execute(PlayerCharacter character, SurvivalCommand command,
            out string errorCode, out string message)
        {
            errorCode = GameplayErrorCodes.InvalidRequest;
            message = "无法执行该操作。";
            if (!character || command.Type == SurvivalCommandType.None)
                return false;
            if (character.IsDead())
                return Failure(GameplayErrorCodes.InvalidRequest, "角色已死亡，不能执行该操作。", out errorCode, out message);

            switch (command.Type)
            {
                case SurvivalCommandType.MoveTo:
                    if (!IsFinite(command.Position) ||
                        Vector3.Distance(character.transform.position, command.Position) > 50f)
                        return Failure(GameplayErrorCodes.OutOfRange, "移动目标无效或距离过远。", out errorCode, out message);
                    character.MoveTo(command.Position);
                    return Success(out errorCode, out message);

                case SurvivalCommandType.Interact:
                {
                    var selectable = Selectable.GetByUID(command.TargetUid);
                    if (!selectable || selectable.gameObject.scene != character.gameObject.scene ||
                        !selectable.CanBeInteracted())
                        return Failure(GameplayErrorCodes.NotFound, "目标已经不存在或无法交互。", out errorCode, out message);
                    if (!selectable.IsInUseRange(character))
                        return Failure(GameplayErrorCodes.OutOfRange, "请走近目标再交互。", out errorCode, out message);
                    character.InteractDirect(selectable, selectable.GetClosestInteractPoint(character.GetInteractCenter()));
                    return Success(out errorCode, out message);
                }

                case SurvivalCommandType.Attack:
                {
                    var target = Selectable.GetByUID(command.TargetUid)?.Destructible;
                    if (!target || target.gameObject.scene != character.gameObject.scene ||
                        !character.Combat.CanAttack(target))
                        return Failure(GameplayErrorCodes.NotFound, "目标已经无法攻击。", out errorCode, out message);
                    if (!character.Combat.IsAttackTargetInRange(target))
                        return Failure(GameplayErrorCodes.OutOfRange, "目标超出近战范围。", out errorCode, out message);
                    character.AttackDirect(target);
                    return Success(out errorCode, out message);
                }

                case SurvivalCommandType.InventoryMove:
                {
                    var source = ResolveInventory(character, command.SourceInventory);
                    var target = ResolveInventory(character, command.TargetInventory);
                    if (source == null || target == null || command.SourceSlot < 0 || command.TargetSlot < 0 ||
                        command.SourceSlot >= source.size || command.TargetSlot >= target.size ||
                        (ReferenceEquals(source, target) && command.SourceSlot == command.TargetSlot))
                        return Failure(GameplayErrorCodes.InvalidRequest, "背包格位置无效。", out errorCode, out message);
                    var sourceItem = source.GetInventoryItem(command.SourceSlot);
                    var targetItem = target.GetInventoryItem(command.TargetSlot);
                    if (sourceItem == null)
                        return Failure(GameplayErrorCodes.NotFound, "源背包格已经为空。", out errorCode, out message);
                    var sourceData = ItemData.Get(sourceItem.item_id);
                    if (!sourceData || (target.type == InventoryType.Bag && sourceData.IsBag()))
                        return Failure(GameplayErrorCodes.InvalidRequest, "物品不能放进目标背包。", out errorCode, out message);
                    if (command.Quantity == 1 && sourceItem.quantity > 1 && targetItem == null &&
                        source.type != InventoryType.Equipment && target.type != InventoryType.Equipment)
                    {
                        source.RemoveItemAt(command.SourceSlot, 1);
                        target.AddItemAt(sourceItem.item_id, command.TargetSlot, 1, sourceItem.durability,
                            UniqueID.GenerateUniqueID());
                        return Success(out errorCode, out message);
                    }
                    if (command.Quantity != 0)
                        return Failure(GameplayErrorCodes.InvalidQuantity, "拆分数量无效。", out errorCode, out message);
                    if (target.type == InventoryType.Equipment)
                    {
                        if (sourceData.type != ItemType.Equipment || sourceData.equip_slot != (EquipSlot)command.TargetSlot)
                            return Failure(GameplayErrorCodes.InvalidRequest, "物品不能装备在该位置。", out errorCode, out message);
                        character.Inventory.EquipItem(source, command.SourceSlot);
                    }
                    else if (source.type == InventoryType.Equipment)
                        character.Inventory.UnequipItemTo(target, (EquipSlot)command.SourceSlot, command.TargetSlot);
                    else if (targetItem != null && sourceItem.item_id == targetItem.item_id)
                    {
                        if (sourceItem.quantity + targetItem.quantity > sourceData.inventory_max)
                            return Failure(GameplayErrorCodes.InvalidQuantity, "目标堆叠已满。", out errorCode, out message);
                        character.Inventory.CombineItems(source, command.SourceSlot, target, command.TargetSlot);
                    }
                    else
                        character.Inventory.SwapItems(source, command.SourceSlot, target, command.TargetSlot);
                    return Success(out errorCode, out message);
                }

                case SurvivalCommandType.InventoryAction:
                    return ExecuteInventoryAction(character, command, out errorCode, out message);

                case SurvivalCommandType.Craft:
                {
                    var craft = CraftData.Get(command.DataId);
                    if (!craft || craft.GetItem() == null || !character.Crafting.CanCraft(craft))
                        return Failure(GameplayErrorCodes.InsufficientItems, "配方或材料条件不满足。", out errorCode, out message);
                    character.Crafting.StartCraftingOrBuilding(craft);
                    return Success(out errorCode, out message);
                }

                case SurvivalCommandType.Build:
                {
                    var craft = CraftData.Get(command.DataId);
                    if (!craft || craft.GetConstruction() == null || !character.Crafting.CanCraft(craft))
                        return Failure(GameplayErrorCodes.InsufficientItems, "建筑配方或材料条件不满足。", out errorCode, out message);
                    if (!IsFinite(command.Position) || !IsFinite(command.Rotation) ||
                        (command.Position - character.transform.position).sqrMagnitude > 100f)
                        return Failure(GameplayErrorCodes.OutOfRange, "建造位置距离角色过远。", out errorCode, out message);
                    character.Crafting.CraftBuildMode(craft);
                    var preview = character.Crafting.GetCurrentBuildable();
                    if (!preview)
                        return Failure(GameplayErrorCodes.InvalidPlacement, "建筑预览创建失败。", out errorCode, out message);
                    preview.transform.rotation = command.Rotation;
                    preview.SetBuildPositionTemporary(command.Position);
                    if (!preview.CheckIfCanBuild())
                    {
                        character.Crafting.CancelCrafting();
                        return Failure(GameplayErrorCodes.InvalidPlacement, "建筑位置有碰撞或地形不合适。", out errorCode, out message);
                    }
                    var constructionCount = PlayerData.Get().built_constructions.Count;
                    character.Crafting.StartCraftBuilding(command.Position);
                    if (!character.Crafting.IsCrafting() &&
                        PlayerData.Get().built_constructions.Count == constructionCount)
                    {
                        character.Crafting.CancelCrafting();
                        return Failure(GameplayErrorCodes.InvalidPlacement, "建造未能开始。", out errorCode, out message);
                    }
                    return Success(out errorCode, out message);
                }

                case SurvivalCommandType.QuestAction:
                    return ExecuteQuestAction(character, command, out errorCode, out message);
            }

            return false;
        }

        private static bool ExecuteQuestAction(PlayerCharacter character, SurvivalCommand command,
            out string errorCode, out string message)
        {
            return SurvivalCampQuestAuthority.Execute(character, command, out errorCode, out message);
        }

        private static bool ExecuteInventoryAction(PlayerCharacter character, SurvivalCommand command,
            out string errorCode, out string message)
        {
            var inventory = ResolveInventory(character, command.SourceInventory);
            var item = inventory?.GetInventoryItem(command.SourceSlot);
            var data = ItemData.Get(item?.item_id);
            if (inventory == null || item == null || !data)
                return Failure(GameplayErrorCodes.NotFound, "物品已经不存在。", out errorCode, out message);

            switch (command.ActionId)
            {
                case "equip":
                    character.Inventory.EquipItem(inventory, command.SourceSlot);
                    break;
                case "unequip":
                    character.Inventory.UnequipItem((EquipSlot)command.SourceSlot);
                    break;
                case "eat":
                    character.Inventory.EatItem(inventory, command.SourceSlot);
                    break;
                case "drop":
                    character.Inventory.DropItem(inventory, command.SourceSlot);
                    break;
                default:
                {
                    var action = data.actions.FirstOrDefault(candidate => candidate && candidate.name == command.ActionId);
                    if (!action)
                        return Failure(GameplayErrorCodes.NotFound, "物品操作不存在。", out errorCode, out message);
                    return Failure(GameplayErrorCodes.InvalidRequest,
                        "该扩展物品操作暂不支持远程执行。", out errorCode, out message);
                }
            }
            return Success(out errorCode, out message);
        }

        private static InventoryData ResolveInventory(PlayerCharacter character, SurvivalInventoryKind kind)
        {
            return kind switch
            {
                SurvivalInventoryKind.Inventory => character.InventoryData,
                SurvivalInventoryKind.Equipment => character.EquipData,
                SurvivalInventoryKind.Bag => character.Inventory.BagData,
                _ => null
            };
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) &&
                   float.IsFinite(value.z) && float.IsFinite(value.w) &&
                   value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > 0.5f;
        }

        private static bool Success(out string errorCode, out string message)
        {
            errorCode = string.Empty;
            message = "操作已由房主确认。";
            return true;
        }

        private static bool Failure(string code, string reason, out string errorCode, out string message)
        {
            errorCode = code;
            message = reason;
            return false;
        }
    }

    [DisallowMultipleComponent]
    public sealed class SurvivalWorldNetworkBridge : MonoBehaviour
    {
        private readonly HashSet<NetworkSurvivalPlayerAdapter> _players = new HashSet<NetworkSurvivalPlayerAdapter>();
        private readonly HashSet<string> _appliedRemoved = new HashSet<string>(StringComparer.Ordinal);
        private bool _isServer;
        private bool _validated;
        private bool _validIds;

        public static SurvivalWorldNetworkBridge GetOrCreate(Scene world, bool isServer)
        {
            var bridge = FindAnyObjectByType<SurvivalWorldNetworkBridge>();
            if (!bridge)
            {
                var root = new GameObject("[SurvivalWorldNetworkBridge]");
                SceneManager.MoveGameObjectToScene(root, world);
                bridge = root.AddComponent<SurvivalWorldNetworkBridge>();
            }
            bridge._isServer |= isServer;
            if (!bridge._validated)
            {
                bridge._validIds = bridge.ValidateUniqueIds(world);
                bridge._validated = true;
            }
            if (!bridge._validIds)
                return null;
            if (!bridge._isServer)
                DisableClientWorldAuthority(world);
            return bridge;
        }

        public void Register(NetworkSurvivalPlayerAdapter player)
        {
            if (player)
                _players.Add(player);
        }

        public SurvivalSnapshot CaptureSnapshot(int localPlayerId, long revision)
        {
            var data = PlayerData.Get();
            var playerData = data.GetPlayerCharacter(localPlayerId);
            return new SurvivalSnapshot
            {
                SchemaVersion = 1,
                Revision = revision,
                LocalPlayerId = localPlayerId,
                Day = data.day,
                DayTime = data.day_time,
                Gold = playerData.gold,
                PrivateInventory = CaptureInventory(localPlayerId),
                PrivateAttributes = playerData.attributes.Select(pair => new SurvivalAttributeSnapshot
                {
                    Type = (int)pair.Key,
                    Value = pair.Value
                }).ToArray(),
                Players = _players.Where(player => player && player.IsReady)
                    .Select(player => player.CapturePublicState()).ToArray(),
                RemovedObjectUids = data.removed_objects.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToArray(),
                HiddenObjectUids = data.hidden_objects.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToArray(),
                WorldInts = data.unique_ids.Select(pair => new SurvivalStringIntSnapshot { Key = pair.Key, Value = pair.Value }).ToArray(),
                WorldFloats = data.unique_floats.Select(pair => new SurvivalStringFloatSnapshot { Key = pair.Key, Value = pair.Value }).ToArray(),
                WorldStrings = data.unique_strings.Select(pair => new SurvivalStringSnapshot { Key = pair.Key, Value = pair.Value }).ToArray(),
                DroppedItems = data.dropped_items.Values.Select(item => new SurvivalDroppedItemSnapshot
                {
                    Uid = item.uid,
                    ItemId = item.item_id,
                    Scene = item.scene,
                    Position = item.pos,
                    Quantity = item.quantity,
                    Durability = item.durability
                }).ToArray(),
                Constructions = data.built_constructions.Values.Select(item => new SurvivalConstructionSnapshot
                {
                    Uid = item.uid,
                    DataId = item.construction_id,
                    Scene = item.scene,
                    Position = item.pos,
                    Rotation = item.rot,
                    Durability = item.durability
                }).ToArray(),
                SceneObjects = data.scene_objects.Values.Select(item => new SurvivalSceneObjectSnapshot
                {
                    Uid = item.uid,
                    Scene = item.scene,
                    Position = item.pos,
                    Rotation = item.rot
                }).ToArray()
            };
        }

        public void ApplySnapshot(SurvivalSnapshot snapshot)
        {
            if (_isServer || !snapshot.HasState)
                return;
            var data = PlayerData.Get();
            data.day = snapshot.Day;
            data.day_time = snapshot.DayTime;
            var playerData = data.GetPlayerCharacter(snapshot.LocalPlayerId);
            playerData.gold = snapshot.Gold;
            playerData.attributes.Clear();
            foreach (var attribute in snapshot.PrivateAttributes ?? Array.Empty<SurvivalAttributeSnapshot>())
                playerData.attributes[(AttributeType)attribute.Type] = attribute.Value;
            ApplyInventory(snapshot.LocalPlayerId, snapshot.PrivateInventory);
            ReplaceWorldData(data, snapshot);
            ApplyRemovedObjects(snapshot.RemovedObjectUids);
            ApplySceneObjects(snapshot.SceneObjects);
            ApplyDynamicItems(snapshot.DroppedItems);
            ApplyConstructions(snapshot.Constructions);
            foreach (var publicPlayer in snapshot.Players ?? Array.Empty<SurvivalPublicPlayerSnapshot>())
            {
                foreach (var player in _players)
                {
                    if (player && player.PlayerId == publicPlayer.PlayerId)
                        player.ApplyPublicState(publicPlayer);
                }
            }
        }

        private static SurvivalInventorySlotSnapshot[] CaptureInventory(int playerId)
        {
            var result = new List<SurvivalInventorySlotSnapshot>();
            AddInventory(result, InventoryData.Get(InventoryType.Inventory, playerId), SurvivalInventoryKind.Inventory);
            AddInventory(result, InventoryData.GetEquip(InventoryType.Equipment, playerId), SurvivalInventoryKind.Equipment);
            var character = PlayerCharacter.Get(playerId);
            if (character && character.Inventory)
                AddInventory(result, character.Inventory.BagData, SurvivalInventoryKind.Bag);
            return result.ToArray();
        }

        private static void AddInventory(List<SurvivalInventorySlotSnapshot> result, InventoryData inventory,
            SurvivalInventoryKind kind)
        {
            if (inventory == null)
                return;
            foreach (var pair in inventory.items)
            {
                var item = pair.Value;
                if (item == null || item.quantity <= 0)
                    continue;
                result.Add(new SurvivalInventorySlotSnapshot
                {
                    Inventory = kind,
                    Slot = pair.Key,
                    ItemId = item.item_id,
                    Quantity = item.quantity,
                    Durability = item.durability,
                    ItemUid = item.uid
                });
            }
        }

        private static void ApplyInventory(int playerId, SurvivalInventorySlotSnapshot[] slots)
        {
            var inventory = InventoryData.Get(InventoryType.Inventory, playerId);
            var equipment = InventoryData.GetEquip(InventoryType.Equipment, playerId);
            inventory.RemoveAll();
            equipment.RemoveAll();
            var incoming = slots ?? Array.Empty<SurvivalInventorySlotSnapshot>();
            foreach (var slot in incoming)
            {
                if (slot.Inventory == SurvivalInventoryKind.Bag)
                    continue;
                var target = slot.Inventory == SurvivalInventoryKind.Equipment ? equipment : inventory;
                target.AddItemAt(slot.ItemId, slot.Slot, slot.Quantity, slot.Durability, slot.ItemUid);
            }
            // The equipped bag determines its own inventory UID. Restore equipment before resolving it.
            var character = PlayerCharacter.Get(playerId);
            var bag = character && character.Inventory ? character.Inventory.BagData : null;
            if (bag == null)
                return;
            bag.RemoveAll();
            foreach (var slot in incoming)
            {
                if (slot.Inventory == SurvivalInventoryKind.Bag)
                    bag.AddItemAt(slot.ItemId, slot.Slot, slot.Quantity, slot.Durability, slot.ItemUid);
            }
        }

        private static void ReplaceWorldData(PlayerData data, SurvivalSnapshot snapshot)
        {
            data.unique_ids = (snapshot.WorldInts ?? Array.Empty<SurvivalStringIntSnapshot>())
                .Where(item => !string.IsNullOrEmpty(item.Key)).ToDictionary(item => item.Key, item => item.Value);
            data.unique_floats = (snapshot.WorldFloats ?? Array.Empty<SurvivalStringFloatSnapshot>())
                .Where(item => !string.IsNullOrEmpty(item.Key)).ToDictionary(item => item.Key, item => item.Value);
            data.unique_strings = (snapshot.WorldStrings ?? Array.Empty<SurvivalStringSnapshot>())
                .Where(item => !string.IsNullOrEmpty(item.Key)).ToDictionary(item => item.Key, item => item.Value);
            data.removed_objects = (snapshot.RemovedObjectUids ?? Array.Empty<string>())
                .Where(uid => !string.IsNullOrEmpty(uid)).Distinct().ToDictionary(uid => uid, _ => 1);
            data.hidden_objects = (snapshot.HiddenObjectUids ?? Array.Empty<string>())
                .Where(uid => !string.IsNullOrEmpty(uid)).Distinct().ToDictionary(uid => uid, _ => 1);
        }

        private void ApplyRemovedObjects(string[] removed)
        {
            var next = new HashSet<string>(removed ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var uid in _appliedRemoved)
            {
                if (!next.Contains(uid))
                    UniqueID.GetByID(uid)?.SetActive(true);
            }
            foreach (var uid in next)
                UniqueID.GetByID(uid)?.SetActive(false);
            _appliedRemoved.Clear();
            _appliedRemoved.UnionWith(next);
        }

        private static void ApplySceneObjects(SurvivalSceneObjectSnapshot[] objects)
        {
            foreach (var item in objects ?? Array.Empty<SurvivalSceneObjectSnapshot>())
            {
                var target = UniqueID.GetByID(item.Uid);
                if (target)
                    target.transform.SetPositionAndRotation(item.Position, item.Rotation);
            }
        }

        private static void ApplyDynamicItems(SurvivalDroppedItemSnapshot[] items)
        {
            var data = PlayerData.Get();
            var incoming = new HashSet<string>((items ?? Array.Empty<SurvivalDroppedItemSnapshot>()).Select(item => item.Uid));
            foreach (var existing in data.dropped_items.Keys.Where(uid => !incoming.Contains(uid)).ToArray())
            {
                var sceneItem = Item.GetByUID(existing);
                if (sceneItem)
                    UnityEngine.Object.Destroy(sceneItem.gameObject);
                data.dropped_items.Remove(existing);
            }
            foreach (var item in items ?? Array.Empty<SurvivalDroppedItemSnapshot>())
            {
                data.AddDroppedItem(item.ItemId, item.Scene, item.Position, item.Quantity, item.Durability, item.Uid);
                if (!Item.GetByUID(item.Uid))
                {
                    var spawned = Item.Spawn(item.Uid);
                    if (spawned)
                        DisableObjectAuthority(spawned.gameObject);
                }
            }
        }

        private static void ApplyConstructions(SurvivalConstructionSnapshot[] constructions)
        {
            var data = PlayerData.Get();
            var incoming = new HashSet<string>((constructions ?? Array.Empty<SurvivalConstructionSnapshot>()).Select(item => item.Uid));
            foreach (var existing in data.built_constructions.Keys.Where(uid => !incoming.Contains(uid)).ToArray())
            {
                var sceneObject = Construction.GetByUID(existing);
                if (sceneObject)
                    UnityEngine.Object.Destroy(sceneObject.gameObject);
                data.built_constructions.Remove(existing);
            }
            foreach (var item in constructions ?? Array.Empty<SurvivalConstructionSnapshot>())
            {
                data.built_constructions[item.Uid] = new BuiltConstructionData
                {
                    uid = item.Uid,
                    construction_id = item.DataId,
                    scene = item.Scene,
                    pos = item.Position,
                    rot = item.Rotation,
                    durability = item.Durability
                };
                if (!Construction.GetByUID(item.Uid))
                {
                    var spawned = Construction.Spawn(item.Uid);
                    if (spawned)
                        DisableObjectAuthority(spawned.gameObject);
                }
            }
        }

        private bool ValidateUniqueIds(Scene world)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var valid = true;
            foreach (var uid in FindObjectsByType<UniqueID>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (uid.gameObject.scene != world || uid.GetComponentInParent<NetworkSurvivalPlayerAdapter>())
                    continue;
                if (string.IsNullOrWhiteSpace(uid.unique_id))
                {
                    Debug.LogError($"[Haven] Multiplayer world object has an empty UniqueID: {uid.name}", uid);
                    valid = false;
                    continue;
                }
                if (!seen.Add(uid.unique_id))
                {
                    Debug.LogError($"[Haven] Multiplayer world has duplicate UniqueID '{uid.unique_id}'.", uid);
                    valid = false;
                }
            }
            return valid;
        }

        private static void DisableClientWorldAuthority(Scene world)
        {
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour.gameObject.scene != world || behaviour.GetComponent<NetworkSurvivalPlayerAdapter>())
                    continue;
                if (IsWorldAuthorityBehaviour(behaviour))
                    behaviour.enabled = false;
            }
        }

        private static void DisableObjectAuthority(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (IsWorldAuthorityBehaviour(behaviour))
                    behaviour.enabled = false;
            }
        }

        private static bool IsWorldAuthorityBehaviour(MonoBehaviour behaviour)
        {
            return behaviour is AnimalWild || behaviour is Character || behaviour is Destructible ||
                   behaviour is Item || behaviour is Construction || behaviour is Buildable ||
                   behaviour is Plant || behaviour is Regrowth;
        }
    }
}
