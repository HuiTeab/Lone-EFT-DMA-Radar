namespace SDK
{
	public readonly partial struct Offsets
	{
		public readonly partial struct GameWorld
		{
			public const uint BtrController = 0x20; // object
			public const uint LocationId = 0xC0; // string
			public const uint LootList = 0x180; // object
			public const uint RegisteredPlayers = 0x198; // object
			public const uint MainPlayer = 0x1E8; // object
			public const uint SynchronizableObjectLogicProcessor = 0x220; // object
			public const uint Grenades = 0x260; // object
			public const uint ExfiltrationController = 0x48; //ExfiltrationController
		}

		public readonly partial struct SynchronizableObject
		{
			public const uint Type = 0x68; // object
		}

		public readonly partial struct SynchronizableObjectLogicProcessor
		{
			public const uint _staticSynchronizableObjects = 0x18; // object
		}

		public readonly partial struct TripwireSynchronizableObject
		{
			public const uint _tripwireState = 0xE4; // object
			public const uint ToPosition = 0x16C; // object
		}

		public readonly partial struct BtrController
		{
			public const uint BtrView = 0x50; // object
		}

		public readonly partial struct BTRView
		{
			public const uint turret = 0x60; // object
			public const uint _previousPosition = 0xB4; // object
		}

		public readonly partial struct BTRTurretView
		{
			public const uint _bot = 0x60; // object
		}

		public readonly partial struct Throwable
		{
			public const uint _isDestroyed = 0x4D; // bool
		}

        public readonly partial struct Player
        {
            public const uint MovementContext = 0x60; // object
            public const uint _playerBody = 0x190; // object
            public const uint Corpse = 0x678; // object
            public const uint Location = 0x868; // string
            public const uint Profile = 0x8E0; // object
            public const uint _playerLookRaycastTransform = 0x9E8; // object
            public const uint ProceduralWeaponAnimation = 0x338;
			
        }

		public readonly partial struct ObservedPlayerView
		{
			public const uint ObservedPlayerController = 0x20; // object
			public const uint Voice = 0x38; // string
			public const uint GroupID = 0x78; // string
			public const uint Side = 0x8C; // object
			public const uint IsAI = 0x98; // bool
			public const uint AccountId = 0xB0; // string
			public const uint PlayerBody = 0xC8; // object
		}

		public readonly partial struct ObservedPlayerController
		{
			public const uint InventoryController = 0x10; // object
			public const uint PlayerView = 0x18; // object
			public const uint MovementController = 0xD8; // object
			public const uint HealthController = 0xE8; // object
		}

		public readonly partial struct InventoryController
		{
			public const uint Inventory = 0x100; // object
		}

		public readonly partial struct Inventory
		{
			public const uint Equipment = 0x18; // object
		}

		public readonly partial struct InventoryEquipment
		{
			public const uint _cachedSlots = 0x90; // object
		}

		public readonly partial struct Slot
		{
			public const uint ContainedItem = 0x48; // object
			public const uint ID = 0x58; // string
		}

		public readonly partial struct ObservedPlayerMovementController
		{
			public const uint ObservedPlayerStateContext = 0x98; // object
		}

		public readonly partial struct ObservedPlayerStateContext
		{
			public const uint Rotation = 0x20; // object
		}

		public readonly partial struct ObservedHealthController
		{
			public const uint HealthStatus = 0x10; // object
			public const uint _player = 0x18; // object
			public const uint _playerCorpse = 0x20; // object
		}

		public readonly partial struct Profile
		{
			public const uint Id = 0x10; // string
			public const uint AccountId = 0x18; // string
			public const uint Info = 0x48; // object
			public const uint QuestsData = 0x98; // object
			public const uint WishlistManager = 0x108; // object
		}

		public readonly partial struct WishlistManager
		{
			public const uint _wishlistItems = 0x30; // object
		}

		public readonly partial struct PlayerInfo
		{
			public const uint Side = 0x48; // object
			public const uint RegistrationDate = 0x4C; // int32_t
			public const uint GroupId = 0x50; // string
			public const uint EntryPoint = 0x28; // string
		}

		public readonly partial struct QuestsData
		{
			public const uint Id = 0x10; // string
			public const uint Status = 0x1C; // object
			public const uint CompletedConditions = 0x28; // object
		}

		public readonly partial struct MovementContext
		{
			public const uint _player = 0x48; // object
			public const uint _rotation = 0xC8; // object
		}

		public readonly partial struct InteractiveLootItem
		{
			public const uint _item = 0xF0; // object
		}

		public readonly partial struct DizSkinningSkeleton
		{
			public const uint _values = 0x30; // object
		}

		public readonly partial struct LootableContainer
		{
			public const uint ItemOwner = 0x168; // object
		}

		public readonly partial struct ItemController
		{
			public const uint RootItem = 0xD0; // object
		}

		public readonly partial struct LootItem
		{
			public const uint Template = 0x60; // object
		}

		public readonly partial struct ItemTemplate
		{
			public const uint ShortName = 0x18; // string
			public const uint QuestItem = 0x34; // bool
			public const uint _id = 0xE0; // object
		}

        public readonly partial struct PlayerBody
        {
            public const uint SkeletonRootJoint = 0x30; // object
        }

        public readonly partial struct ExfiltrationController
        {
            public const uint SecretExfilitranionController = 0x18; // EFT.Interactive.SecretExfiltrations.SecretExfilitranionController
            public const uint ExfiltrationPoints = 0x20; // EFT.Interactive.ExfiltrationPoint[]
            public const uint ScavExfiltrationPoints = 0x28; // EFT.Interactive.ScavExfiltrationPoint[]
            public const uint SecretExfiltrationPoints = 0x30; // EFT.Interactive.SecretExfiltrations.SecretExfiltrationPoint[]
        }

        public readonly partial struct ExfiltrationPoint //EFT.Interactive.ExfiltrationPoint
        {
            public const uint _status = 0x58; // EFT.Interactive.EExfiltrationStatus
            public const uint Settings = 0x98;// EFT.Interactive.ExitTriggerSettings
            public const uint EligibleEntryPoints = 0xC0; // [C0] EligibleEntryPoints : string[]
            public const uint EligibleIds = 0xF8; //[F8] EligibleIds : System.Collections.Generic.List<string>
        }

        public readonly partial struct ExitTriggerSettings
        {
            public const uint Id = 0x10; // string
            public const uint Name = 0x18; // string
            public const uint EntryPoints = 0x40; // string
        }

        public readonly partial struct ProceduralWeaponAnimation
        {
            public const uint ShotNeedsFovAdjustments = 0x433;
            public const uint Breath = 0x38;
            public const uint Mask = 0x30;
            public const uint Shootingg = 0x58;
            public const uint _isAiming = 0x145;
            public const uint _optics = 0x180;
            public const uint _shotDirection = 0x1c8;
            public const uint _aimingSpeed = 0x164;
            public const uint HandsContainer = 0x20;
            public const uint PositionZeroSum = 0x31c;
        }

        public readonly partial struct SightNBone
        {
            public const uint Mod = 0x10; // EFT.InventoryLogic.SightComponent
        }
        public readonly partial struct SightComponent
        {
            public const uint OpticCalibrationPoints = 0x18;
            public const uint ScopeZoomValue = 0x3c;
            public const uint ScopesCurrentCalibPointIndexes = 0x28;
            public const uint ScopesSelectedModes = 0x30;
            public const uint SelectedScope = 0x38;
            public const uint _template = 0x20;
        }

        public readonly partial struct SightInterface
        {
            public const uint Zooms = 0x1B8; // System.Single[][]
        }
        public readonly partial struct ThermalVision
        {
            public const uint ChromaticAberrationThermalShift = 0x68;
            public const uint IsFpsStuck = 0x22;
            public const uint IsGlitch = 0x24;
            public const uint IsMotionBlurred = 0x23;
            public const uint IsNoisy = 0x21;
            public const uint IsPixelated = 0x25;
            public const uint On = 0x20;
            public const uint UnsharpRadiusBlur = 0x90;
        }
        public readonly partial struct QuestData
        {
            public const uint Status = 0x1c; // EFT.Quests.EQuestStatus
            public const uint CompletedConditions = 0x28; //System.Collections.Generic.HashSet<EFT.MongoID>
            public const uint Id = 0x10; // string
            public const uint Template = 0x38; // string
        }
        public readonly partial struct QuestStatus
        {
            public const uint Value = 0x10; // public Int32 value__; // 0x10
        }

        public readonly partial struct QuestTemplate
        {
            public const uint Conditions = 0x60; // EFT.Quests.ConditionsDict
        }
    }

	public readonly partial struct Enums
	{
		public enum EPlayerSide
		{
			Usec = 1,
			Bear = 2,
			Savage = 4,
		}

		[Flags]
		public enum ETagStatus
		{
			Unaware = 1,
			Aware = 2,
			Combat = 4,
			Solo = 8,
			Coop = 16,
			Bear = 32,
			Usec = 64,
			Scav = 128,
			TargetSolo = 256,
			TargetMultiple = 512,
			Healthy = 1024,
			Injured = 2048,
			BadlyInjured = 4096,
			Dying = 8192,
			Birdeye = 16384,
			Knight = 32768,
			BigPipe = 65536,
			BlackDivision = 131072,
			VSRF = 262144,
		}

		[Flags]
		public enum EMemberCategory
		{
			Default = 0,
			Developer = 1,
			UniqueId = 2,
			Trader = 4,
			Group = 8,
			System = 16,
			ChatModerator = 32,
			ChatModeratorWithPermanentBan = 64,
			UnitTest = 128,
			Sherpa = 256,
			Emissary = 512,
			Unheard = 1024,
		}

		public enum SynchronizableObjectType
		{
			AirDrop = 0,
			AirPlane = 1,
			Tripwire = 2,
		}

		public enum ETripwireState
		{
			None = 0,
			Wait = 1,
			Active = 2,
			Exploding = 3,
			Exploded = 4,
			Inert = 5,
		}

		public enum EExfiltrationStatus
        {
            NotPresent = 1,
            UncompleteRequirements = 2,
            Countdown = 3,
            RegularMode = 4,
            Pending = 5,
            AwaitsManualActivation = 6,
            Hidden = 7,
        }

        public enum ERequirementState
        {
            None = 0,
            Empty = 1,
            TransferItem = 2,
            WorldEvent = 3,
            NotEmpty = 4,
            HasItem = 5,
            WearsItem = 6,
            EmptyOrSize = 7,
            SkillLevel = 8,
            Reference = 9,
            ScavCooperation = 10,
            Train = 11,
            Timer = 12,
            SecretTransferItem = 13,
        }

        public enum EQuestStatus
        {
            Locked = 0,
            AvailableForStart = 1,
            Started = 2,
            AvailableForFinish = 3,
            Success = 4,
            Fail = 5,
            FailRestartable = 6,
            MarkedAsFailed = 7,
            Expired = 8,
            AvailableAfter = 9,
            AutoStart = 10,
        }
	}
}
