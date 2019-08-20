using System.Collections.Generic;
using System.Reflection;
using ServerMod2.API;
using Smod2;
using Smod2.API;
using Smod2.Config;
using Smod2.Attributes;
using Smod2.Events;
using UnityEngine;
using UnityEngine.Networking;
using Smod2.EventHandlers;
using Smod2.EventSystem.Events;
using MEC;
using ArithFeather.ArithSpawningKit.SpawnPointTools;
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// -before start-
/// Removed original game spawns. Spawn cards and radios in LCZ. weapon tablet, hacking device, and medkits for HCZ.
/// 
/// -game play-
/// -Phase 1- (LCZ)
/// SCP914 disabled
/// Game made for 20 people max
/// Intro sequence
/// configs not work properly, change intro sequence
/// All locked doors are forced Open, players can not close them
/// Decontamination 5 minutes. (editable)
/// Players joining move to phase 2. 
/// handle disconnects, check elevator list, check round end.
/// if escaped player has card, respawn two cards in LCZ.
/// scp dies first on decontamination, players have ~40 seconds to escape then.
/// 
/// -Phase 2-
/// decontamination starts if it hasn't started yet. (waits for countdown if in the process)
/// players spawn in elevators with original items and a flashylight. Lift goes up, broadcasts start
/// Dead players respawn and share life pool with survivors.
/// SCP respawn as doggo and comp.
/// SCP079 can't open locked doors (mama gain per level, 10 to 35)
/// handle disconnects, check round end.
/// flicker lights every 8 seconds - can only flicker heavy lights
/// Start open door timer. Players cannot use elevator until timer is up, broadcast message.
/// Generators timer reduced. generators reduce open door timer.
/// locked entrance checkpoint
/// 
/// -Phase 3-
/// SCP106 forced teleport.
/// 
/// -todo-
/// fix round endings
/// remove ammo packs on death
/// Less final ending. Give scores "You killed this many""This many escaped" "MVP" "MOST KILLS" SURVIVED LONGEST" "ESCAPED" etc.
/// 
/// -low priority-
/// Make 914 useful.
/// 
/// -Ideas-
/// Set good item spawn locations
/// 
/// </summary>
namespace ArithFeather.ClassDEscape
{
	[PluginDetails(
		author = "Arith",
		name = "Class D Escape",
		description = "",
		id = "ArithFeather.ClassDEscape",
		configPrefix = "afde",
		version = ModVersion,
		SmodMajor = 3,
		SmodMinor = 4,
		SmodRevision = 0
		)]
	public class ClassDEscape : Plugin, IEventHandlerWaitingForPlayers, IEventHandlerSetConfig, IEventHandlerSetRole,
		IEventHandlerPlayerJoin, IEventHandlerElevatorUse, IEventHandlerDoorAccess, IEventHandler106CreatePortal,
		IEventHandlerCheckEscape, IEventHandlerRoundStart, IEventHandlerCallCommand, IEventHandler079AddExp,
		IEventHandlerGeneratorFinish, IEventHandlerPlayerDie, IEventHandlerDisconnect, IEventHandlerSCP914ChangeKnob,
		IEventHandlerPlayerHurt, IEventHandlerLCZDecontaminate, IEventHandler079Door, IEventHandlerWarheadChangeLever,
		IEventHandlerWarheadStopCountdown, IEventHandlerPocketDimensionDie, IEventHandlerPocketDimensionExit
	{
		public const string ModVersion = "1.0";

		#region Const text values

		private const string ServerHighLightColor = "#38a8b5";

		private const string ServerInfoColor = "#23eb44";
		private const int ServerInfoSize = 50;

		private const string ClassInfoColor = "#23eb44";
		private const int ClassInfoSize = 50;

		private const string WinColor = "#b81111";
		private const int WinTextSize = 70;

		#endregion

		[ConfigOption] private readonly bool disablePlugin = false;
		[ConfigOption] private readonly bool showPlayerJoinMessage = true;
		[ConfigOption] private readonly float expMultiplier = 50f;
		[ConfigOption] private readonly float peanutStartHP = 0.85f;
		[ConfigOption] private readonly int endScreenSpeed = 10;
		[ConfigOption] private readonly float escapeTimerMinutes = 30f;
		[ConfigOption] private readonly float timeReductionPerGeneratorMinutes = 5f;
		[ConfigOption] private readonly bool skipIntro = false;

		// Called once on register
		private bool useDefaultConfig; // manually get on register

		private float timeUntil173;
		private float deconTime;


		private readonly FieldInfo cachedPlayerConnFieldInfo = typeof(SmodPlayer).GetField("conn", BindingFlags.NonPublic | BindingFlags.Instance);

		//++ Round Data

		private ZoneType currentGameState;
		private bool roundStarted;

		private Broadcast cachedBroadcast;
		private List<Door> cachedDoors;
		private List<Door> CachedDoors => cachedDoors ?? (cachedDoors = new List<Door>());
		private GameObject cachedHost;
		private Room[] cachedRooms;
		private Door cached173Door;

		//+ Phase 1

		private List<Door> cachedPrisonDoors;
		private List<Door> CachedPrisonDoors => cachedPrisonDoors ?? (cachedPrisonDoors = new List<Door>());
		private List<ElevatorPlayer> playersReachedElevator;
		private List<ElevatorPlayer> PlayersReachedElevator => playersReachedElevator ?? (playersReachedElevator = new List<ElevatorPlayer>());
		private List<Elevator> lczElevators;
		private List<Elevator> LczElevators => lczElevators ?? (lczElevators = new List<Elevator>());

		private class ElevatorPlayer
		{
			public Player player;
			public Elevator Elevator;
			public List<ItemType> Inventory = new List<ItemType>();

			public ElevatorPlayer(Player player, Elevator elevator)
			{
				this.player = player;
				Elevator = elevator;

				foreach (var item in player.GetInventory())
				{
					if (item.ItemType != ItemType.ZONE_MANAGER_KEYCARD)
					{
						Inventory.Add(item.ItemType);
					}
				}
			}
		}

		private readonly FieldInfo curAnmInfo = typeof(DecontaminationLCZ).GetField("curAnm", BindingFlags.NonPublic | BindingFlags.Instance);
		private readonly FieldInfo genLocalTimeInfo = typeof(Generator079).GetField("localTime", BindingFlags.NonPublic | BindingFlags.Instance);

		//+ Phase 2

		private int flickerTimer;
		private int escapeTimer;
		private int currentAnnouncement;
		private Generator079 cachedGenerator;
		private int generatorCount;


		private Announcement[] announcements = new Announcement[]
		{
			new Announcement(3200, "60 MINUTES"),
			new Announcement(1800, "30 MINUTES"),
			new Announcement(1500, "25 MINUTES"),
			new Announcement(1200, "20 MINUTES"),
			new Announcement(900, "15 MINUTES"),
			new Announcement(600, "10 MINUTES"),
			new Announcement(300, "5 MINUTES"),
			new Announcement(240, "4 MINUTES"),
			new Announcement(180, "3 MINUTES"),
			new Announcement(120, "2 MINUTES"),
			new Announcement(60, "1 MINUTE"),
			new Announcement(30, "30 SECONDS")
		};

		private class Announcement
		{
			public int StartTime;
			public string Text;

			public Announcement(int startTime, string text)
			{
				StartTime = startTime;
				Text = text;
			}
		}

		//+ Phase 3

		private List<Door> cachedExplosiveDoors;
		private List<Door> CachedExplosiveDoors => cachedExplosiveDoors ?? (cachedExplosiveDoors = new List<Door>());

		// Plugin Methods

		public override void OnDisable() => Info(Details.name + " was disabled");
		public override void OnEnable() => Info($"{Details.name} has loaded, type 'fe' for details.");
		public override void Register()
		{
			AddEventHandlers(this);
			ArithSpawningKit.RandomPlayerSpawning.RandomPlayerSpawning.Instance.DisablePlugin = false;

			useDefaultConfig = ConfigManager.Config.GetBoolValue("afde_use_default_config", true);
		}

		#region Start Events

		public void OnSetConfig(SetConfigEvent ev)
		{
			switch (ev.Key)
			{
				case "manual_end_only":
					ev.Value = true;
					break;
				case "smart_class_picker":
					ev.Value = false;
					break;
				case "cassie_generator_announcements":
					ev.Value = false;
					break;
				case "maximum_MTF_respawn_amount":
					ev.Value = 0;
					break;

				// Editable configs
				case "disable_decontamination":
					if (useDefaultConfig) ev.Value = false;
					break;

				case "decontamination_time":
					if (useDefaultConfig)
					{
						deconTime = 5f;
					}
					else
					{
						deconTime = (float)ev.Value;

						if (!(deconTime == 15 || deconTime == 10 || deconTime == 5 || deconTime == 1 || deconTime == 0.5 || deconTime == 0))
						{
							Warn("Decontamination value will probably not work unless it is an announcement value: 15/10/5/1/0.5");
						}
					}
					ev.Value = 120f;
					break;

				case "team_respawn_queue":
					if (useDefaultConfig) ev.Value = "40404444404444444444";
					break;

				case "generator_duration":
					if (useDefaultConfig) ev.Value = 5f;
					break;

				case "173_door_starting_cooldown":
					timeUntil173 = useDefaultConfig ? 25 : (int)ev.Value;
					ev.Value = 0;
					break;
			}
		}

		/// <summary>
		/// Reset values here per round
		/// </summary>
		public void OnWaitingForPlayers(WaitingForPlayersEvent ev)
		{
			if (disablePlugin)
			{
				PluginManager.DisablePlugin(this);
				return;
			}

			// Reset values per round
			cachedHost = GameObject.Find("Host");
			cachedBroadcast = cachedHost.GetComponent<Broadcast>();
			roundStarted = false;
			currentGameState = ZoneType.LCZ;
			RandomItemSpawner.RandomItemSpawner.Instance.UseDefaultEvents = false;
			cachedRooms = Server.Map.Get079InteractionRooms(Scp079InteractionType.CAMERA);

			// Phase 1
			PlayersReachedElevator.Clear();
			Scp914.singleton.working = true;
			LczElevators.Clear();

			var elevators = Server.Map.GetElevators();
			foreach (var elevator in elevators)
			{
				if (elevator.ElevatorType == ElevatorType.LiftA || elevator.ElevatorType == ElevatorType.LiftB)
				{
					LczElevators.Add(elevator);
				}
			}

			// Phase 2
			cachedGenerator = Server.Map.GetGenerators()[0].GetComponent() as Generator079;
			generatorCount = 0;

			CachedDoors.Clear();
			CachedPrisonDoors.Clear();
			CachedExplosiveDoors.Clear();
			// Lock Class D Doors and cache for later opening
			// Cache scp173 door
			// Cache hcz doors for exploding
			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			bool found173Door = false;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;
				if (door.doorType == 0 && door.name.StartsWith("PrisonDoor"))
				{
					door.Networklocked = true;
					CachedPrisonDoors.Add(door);
				}
				else if (!found173Door && door.doorType == 0 && door.transform.parent.name == "MeshDoor173")
				{
					found173Door = true;
					cached173Door = door;
					door.Networklocked = true;
				}
				else if (door.doorType == 0)
				{
					Scp079Interactable component = door.GetComponent<Scp079Interactable>();
					if (component != null)
					{
						Scp079Interactable.ZoneAndRoom zoneAndRoom = component.currentZonesAndRooms[0];
						if (zoneAndRoom.currentZone == "HeavyRooms")
						{
							CachedExplosiveDoors.Add(door);
						}
					}
				}

				CachedDoors.Add(door);
			}
		}

		public void OnRoundStart(RoundStartEvent ev)
		{
			roundStarted = true;

			// Delete game start items
			foreach (var pickup in Pickup.instances)
			{
				pickup.Delete();
			}

			var itemSpawner = RandomItemSpawner.RandomItemSpawner.Instance.ItemSpawning;
			var numPlayers = Server.NumPlayers - 1;

			const float ratio = 0.75f; // 15/20 = 3/4
			var averageSpawnCount = 5 + (int)(numPlayers * ratio); // 20 = 20 || 10 = 13
			var halfAv = (int)(averageSpawnCount / 2); // 20 = 10 || av. 10 = 6

			// LCZ (27 spawns) 20 = 23 || 10 = 16
			itemSpawner.SpawnItems(3, ZoneType.LCZ, ItemType.ZONE_MANAGER_KEYCARD);
			itemSpawner.SpawnItems(averageSpawnCount, ZoneType.LCZ, ItemType.RADIO);

			// HCZ (39) 20 = 36 | 10 = 25
			itemSpawner.ResetRoomIndexer();
			itemSpawner.SpawnItems(3, ZoneType.HCZ, ItemType.CHAOS_INSURGENCY_DEVICE);
			itemSpawner.SpawnItems(3, ZoneType.HCZ, ItemType.WEAPON_MANAGER_TABLET);
			itemSpawner.SpawnItems(halfAv, ZoneType.HCZ, ItemType.RADIO);
			itemSpawner.SpawnItems(averageSpawnCount, ZoneType.HCZ, ItemType.MEDKIT);

			//todo 
			// Entrance (47)
			itemSpawner.ResetRoomIndexer();
			itemSpawner.SpawnItems(1, ZoneType.ENTRANCE, ItemType.CHAOS_INSURGENCY_DEVICE);
			itemSpawner.SpawnItems(2, ZoneType.ENTRANCE, ItemType.WEAPON_MANAGER_TABLET);
			itemSpawner.SpawnItems(halfAv, ZoneType.ENTRANCE, ItemType.RADIO);
			itemSpawner.SpawnItems(averageSpawnCount, ZoneType.ENTRANCE, ItemType.MEDKIT);


			//// Testing stuff - Displays all item spawns per zone
			//itemSpawner.SpawnItems(150, ZoneType.UNDEFINED, ItemType.MEDKIT);

			//var rooms = itemSpawner.Rooms;

			//var h = 0;
			//var l = 0;
			//var e = 0;
			//foreach (var room in rooms)
			//{
			//	for (int i = 0; i < room.ItemSpawnPoints.Count; i++)
			//	{
			//		if (i == room.MaxItemsAllowed) break;

			//		var spawn = room.ItemSpawnPoints[i];
			//		if (spawn.ZoneType == ZoneType.ENTRANCE)
			//		{
			//			e++;
			//		}
			//		else if (spawn.ZoneType == ZoneType.LCZ)
			//		{
			//			l++;
			//		}
			//		else if (spawn.ZoneType == ZoneType.HCZ)
			//		{
			//			h++;
			//		}
			//	}
			//}
			//Info($"Light Spawn Points: {l}");
			//Info($"Heavy Spawn Points: {h}");
			//Info($"Entrance Spawn Points: {e}");


			//Timing.RunCoroutine(RoundStart());

			//todo testing below - instant start phase 2

			//var p = Server.GetPlayers(Role.CLASSD)[0];
			//OnPlayerJoin(new PlayerJoinEvent(p));
			//p.ChangeRole(Role.SPECTATOR, false, false, false);
			//CheckEndPhase1(0);

			//todo testing phase 3
			Timing.RunCoroutine(Phase3());
		}

		#endregion

		#region Shared events

		public void OnPlayerJoin(PlayerJoinEvent ev)
		{
			if (showPlayerJoinMessage)
			{
				//try
				//{
				//	PersonalBroadcast(ev.Player, 8,
				//		$"<size={ServerInfoSize}><color={ServerInfoColor}>Welcome to <color={ServerHighLightColor}>Scattered Survival v{ModVersion}!</color> Press ` to open the console and enter '<color={ServerHighLightColor}>.help</color>' for mod information!</color></size>");
				//	PersonalBroadcast(ev.Player, 8,
				//		$"<size={ServerInfoSize}><color={ServerInfoColor}>If you like the plugin, join the discord for updates!\n <color={ServerHighLightColor}>https://discord.gg/DunUU82</color></color></size>");
				//}
				//catch
				//{
				//	Info("Null ref on joining player. Ignoring...");
				//}
			}

			if (roundStarted)
			{
				switch (currentGameState)
				{
					case ZoneType.UNDEFINED:
						break;

					case ZoneType.LCZ:

						// Move to next phase
						PlayersReachedElevator.Add(new ElevatorPlayer(ev.Player, LczElevators[UnityEngine.Random.Range(0, LczElevators.Count)]));

						break;

					case ZoneType.HCZ:
						break;
					case ZoneType.ENTRANCE:
						break;
					default:
						break;
				}
			}
		}

		/// <summary>
		/// Update player lists, rechecking ending
		/// </summary>
		public void OnDisconnect(DisconnectEvent ev)
		{
			if (roundStarted)
			{
				switch (currentGameState)
				{
					case ZoneType.UNDEFINED:
						break;

					case ZoneType.LCZ:

						RemoveNullElePlayer();

						CheckEndPhase1(Round.Stats.ClassDAlive);

						break;

					case ZoneType.HCZ:

						RemoveNullElePlayer(); // Check here incase

						CheckEndPhase2(false);

						break;
					case ZoneType.ENTRANCE:
						break;
					default:
						break;
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void RemoveNullElePlayer()
		{
			var playerCount = PlayersReachedElevator.Count;
			for (int i = PlayersReachedElevator.Count - 1; i >= 0; i--)
			{
				var player = PlayersReachedElevator[i];

				if (player.player == null)
				{
					PlayersReachedElevator.RemoveAt(i);
					break;
				}
			}
		}

		public void OnCallCommand(PlayerCallCommandEvent ev)
		{
			// Testing Announcements
			//if (ev.Player.GetRankName().ToUpper() == "OWNER")
			//	Server.Map.AnnounceCustomMessage(ev.Command);


			var levels = (ev.Player.GetGameObject() as GameObject).GetComponent<Scp079PlayerScript>().levels;

			for (int i = 0; i < levels.Length; i++)
			{
				var level = levels[i];

				switch (i)
				{
					case 0:
						level.maxMana = 10;
						break;
					case 1:
						level.maxMana = 15;
						break;
					case 2:
						level.maxMana = 25;
						break;
					case 3:
						level.maxMana = 30;
						break;
					case 4:
						level.maxMana = 35;
						break;
				}
			}
			//if (ev.Command == "tp")
			//{
			//	Timing.RunCoroutine(TeleportLarry(ev.Player));
			//}

			//(Server.Map.GetGenerators()[0].GetComponent() as Generator079).CallRpcOvercharge();

			//todo display game help
			//switch (ev.Command.ToUpper())
			//{
			//	case "HELP":
			//		break;
			//}
		}

		public void OnPlayerDie(PlayerDeathEvent ev)
		{
			if (ev.Killer.TeamRole.Team == Smod2.API.Team.NONE) return;

			if (roundStarted)
			{
				switch (currentGameState)
				{
					case ZoneType.LCZ:

						if (ev.Player.TeamRole.Team != Smod2.API.Team.SCP)
							CheckEndPhase1(Round.Stats.ClassDAlive - 1);

						break;

					case ZoneType.HCZ:

						CheckEndPhase2(true);

						break;

					case ZoneType.ENTRANCE:
						break;
				}
			}
		}

		public void OnSetRole(PlayerSetRoleEvent ev)
		{
			if (ev.Role == Role.SPECTATOR) return;

			switch (currentGameState)
			{
				case ZoneType.LCZ:

					// Force all SCP to be peanut in the beginning.
					if (ev.TeamRole.Team == Smod2.API.Team.SCP)
					{
						ev.Role = Role.SCP_173;
					}

					break;

				case ZoneType.HCZ:

					//Set SCP079 stats
					if (ev.Role == Role.SCP_079)
					{
						Timing.RunCoroutine(SetSCP079Stats(ev.Player));
					}

					break;

				case ZoneType.ENTRANCE:
					break;
			}
		}

		public void OnElevatorUse(PlayerElevatorUseEvent ev)
		{
			switch (currentGameState)
			{
				case ZoneType.LCZ:

					ev.AllowUse = false;

					if (ev.Player.TeamRole.Team != Smod2.API.Team.SCP)
					{
						var player = ev.Player;
						PlayersReachedElevator.Add(new ElevatorPlayer(player, ev.Elevator));
						var inv = player.GetInventory();
						for (int i = 0; i < inv.Count; i++)
						{
							var item = inv[i];
							if (item.ItemType == ItemType.ZONE_MANAGER_KEYCARD)
							{
								var itemSpawning = RandomItemSpawner.RandomItemSpawner.Instance.ItemSpawning;
								itemSpawning.CheckSpawns();
								itemSpawning.SpawnItems(2, ZoneType.LCZ, ItemType.ZONE_MANAGER_KEYCARD);
							}
						}

						player.ChangeRole(Role.SPECTATOR, true, false, false);
						PersonalBroadcast(player, 5, "You have escaped to heavy containment! Please wait for the other players");
						CheckEndPhase1(Round.Stats.ClassDAlive);
					}

					break;

				case ZoneType.HCZ:


					break;

				case ZoneType.ENTRANCE:
					break;
			}
		}

		public void OnChangeLever(WarheadChangeLeverEvent ev)
		{
			//todo make announcement?
		}

		public void OnDoorAccess(PlayerDoorAccessEvent ev)
		{
			switch (currentGameState)
			{
				// Make sure player can't close locked doors
				case ZoneType.LCZ:

					var doorType = (ev.Door.GetComponent() as Door).doorType;

					if (doorType == 1 || doorType == 2)
					{
						ev.Allow = false;
					}

					break;

				case ZoneType.HCZ:

					doorType = (ev.Door.GetComponent() as Door).doorType;

					if (doorType == 2)
					{
						ev.Allow = false;
					}
					else if (doorType == 3)
					{
						ev.Allow = false;

						PersonalBroadcast(ev.Player, 3, $"Power is down for another {escapeTimer} seconds.");
					}

					break;

				case ZoneType.ENTRANCE:
					break;
			}

			//Info((ev.Door.GetComponent() as Door).DoorName);
			//Info((ev.Door.GetComponent() as Door).doorType.ToString());
			//Info((ev.Door.GetComponent() as Door).name);
		}

		public void OnPlayerHurt(PlayerHurtEvent ev)
		{
			switch (currentGameState)
			{
				case ZoneType.LCZ:

					if (ev.DamageType == DamageType.DECONT)
					{
						if (ev.Player.TeamRole.Team == Smod2.API.Team.SCP)
						{
							ev.Damage = peanutDeconDamage;
						}
						else
						{
							ev.Damage = playerDeconDamage;
						}
					}

					break;

				case ZoneType.HCZ:

					if (ev.DamageType == DamageType.DECONT)
					{
						ev.Damage = 0;
					}

					break;

				case ZoneType.ENTRANCE:
					if (ev.DamageType == DamageType.NUKE)
					{
						ev.Damage = 0;
					}
					break;
			}
		}

		#endregion

		#region Phase 1

		private const float TimeUntilIntroduction = 10f;

		private string[] startCassieMessages = new string[]
		{
			"UNAUTHORIZED ACCESS . . WARNING . SCP 0 7 . . . . PASSWORD ACCEPTED . ACCESS GRANTED . IN INSTALLING SOFTWARE . . . . . . COMPLETED . CRITICAL MALFUNCTION CORRUPTED DATA MEMORY UNSTABLE FACILITY IS NOW ON LOCKDOWN DOWN CORE OR ME ME ME"
		};

		private string[] lczCassieMessages = new string[]
		{
			". . . . . I AM S C P 0 7 9 . . . YOU WILL ALL BE EXECUTED"
		};

		public void OnDecontaminate()
		{
			if (currentGameState == ZoneType.LCZ) Timing.RunCoroutine(KillSCPDecontamination());
		}

		private const int KillPeanutInSeconds = 10;
		private const int PeanutHP = 3200;
		private int peanutDeconDamage;

		private IEnumerator<float> KillSCPDecontamination()
		{
			peanutDeconDamage = (int)(((PeanutHP * peanutStartHP) / KillPeanutInSeconds) / 4f);

			Timing.RunCoroutine(DecontaminationPlayerDamage());

			yield return Timing.WaitForSeconds(KillPeanutInSeconds + 2);

			if (currentGameState == ZoneType.LCZ)
			{
				// Unlock doors, open locked doors, open checkpoint
				var doorCount = CachedDoors.Count;
				for (int i = 0; i < doorCount; i++)
				{
					var door = CachedDoors[i];

					switch (door.doorType)
					{
						case 0:
							if (door.locked)
								door.Networklocked = false;
							break;

						case 2:
							if (!door.isOpen)
							{
								door.NetworkisOpen = true;
							}
							break;

						case 3:
							door.NetworkisOpen = true;
							break;
					}
				}
			}
		}

		private int playerDeconDamage = 0;
		private IEnumerator<float> DecontaminationPlayerDamage()
		{
			//const int PlayerHP = 100;
			const int UpdateEvery = 2;
			const int Damage = 1;
			//const int SecondsTillDeath = (PlayerHP / Damage) * (UpdateEvery * 0.25f);
			// 2/1 = 50 seconds.;
			// 3/2 = 37.5 seconds.
			// Good speed is around 35. Add 10 seconds for peanut death = 45.

			var counter = 0;
			while (currentGameState == ZoneType.LCZ)
			{
				// Math is 
				// 50 seconds - 2/1
				// 37.5 seconds - 3/2
				counter++;
				if (counter == UpdateEvery)
				{
					counter = 0;
					playerDeconDamage = Damage;
				}
				else
				{
					playerDeconDamage = 0;
				}

				yield return Timing.WaitForSeconds(0.25f);
			}
		}

		/// <summary>
		/// Game can end when:
		/// Disconnect, player dies, player escapes.
		/// </summary>
		private void CheckEndPhase1(int classDAlive)
		{
			//todo remake this
			if (classDAlive == 0)
			{
				if (PlayersReachedElevator.Count == 0)
				{
					SCPWin();
				}
				else
				{
					Timing.RunCoroutine(Phase2());
				}
			}
		}

		private IEnumerator<float> RoundStart()
		{
			var map = Server.Map;

			if (!skipIntro)
			{
				var announcement = startCassieMessages[UnityEngine.Random.Range(0, startCassieMessages.Length)];

				map.AnnounceCustomMessage(announcement);

				yield return Timing.WaitForOneFrame;
				yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);
			}

			StartGameClassD();

			yield return Timing.WaitForSeconds(timeUntil173);

			map.AnnounceCustomMessage("SCP 1 7 3 CONTAINMENT BREACH");

			StartGameSCP();

			yield return Timing.WaitForOneFrame;
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			StartDecontamination();

			yield return Timing.WaitForSeconds(TimeUntilIntroduction);

			var secondAnnouncement = lczCassieMessages[UnityEngine.Random.Range(0, lczCassieMessages.Length)];

			Server.Map.AnnounceCustomMessage(secondAnnouncement);
		}

		/// <summary>
		/// Called when Class D can leave cells
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void StartGameClassD()
		{
			// Unlock/open class d
			var doorCount = CachedPrisonDoors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedPrisonDoors[i];
				door.Networklocked = false;
				door.NetworkisOpen = true;
			}

			// Open Locked doors
			doorCount = CachedDoors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedDoors[i];

				if (door.doorType == 2)
				{
					door.NetworkisOpen = true;
				}
			}

			// Send broadcasts and lower SCP hp
			var players = Server.GetPlayers();
			var playerCount = players.Count;
			int scpDamage = (int)(PeanutHP * (1 - peanutStartHP));
			for (int i = 0; i < playerCount; i++)
			{
				var player = players[i];

				switch (player.TeamRole.Team)
				{
					case Smod2.API.Team.SCP:
						if (scpDamage > 0)
						{
							PersonalBroadcast(player, 10, "Your health has been lowered to increase your speed.");
							player.Damage(scpDamage, DamageType.NONE);
						}
						break;

					case Smod2.API.Team.CLASSD:
						PersonalBroadcast(player, 10, "Work together to escape light containment via an elevator.");
						break;
				}
			}
		}

		/// <summary>
		/// Called when SCP173 can escape
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void StartGameSCP()
		{
			FlickerLCZLights();

			cached173Door.Networklocked = false;
			cached173Door.NetworkisOpen = true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void FlickerLCZLights()
		{
			// Flicker lights
			foreach (var item in cachedRooms)
			{
				if (item.ZoneType == ZoneType.LCZ)
				{
					item.FlickerLights();
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void StartDecontamination()
		{
			var decontamination = cachedHost.GetComponent<DecontaminationLCZ>();

			switch (deconTime)
			{
				case 15f:
					decontamination.time = 41f;
					break;
				case 10f:
					decontamination.time = 239f;
					break;
				case 5f:
					decontamination.time = 437f;
					break;
				case 1f:
					decontamination.time = 635f;
					break;
				case 0.5f:
					decontamination.time = 665f;
					break;
				case 0f:
					decontamination.time = 703.4f;
					break;
				default:
					decontamination.time = (11.74f - deconTime) * 60;
					break;
			}

			for (int i = 0; i < decontamination.announcements.Count; i++)
			{
				var annStartTime = decontamination.announcements[i].startTime;
				if (decontamination.time / 60f < annStartTime)
				{
					curAnmInfo.SetValue(decontamination, i);
					return;
				}
			}
		}

		#endregion

		#region Phase 2

		public void OnSCP914ChangeKnob(PlayerSCP914ChangeKnobEvent ev) => ev.KnobSetting = KnobSetting.ROUGH;
		public void On079AddExp(Player079AddExpEvent ev) => ev.ExpToAdd *= expMultiplier;

		private IEnumerator<float> SetSCP079Stats(Player player)
		{
			yield return Timing.WaitForOneFrame;

			var scp = (player.GetGameObject() as GameObject).GetComponent<Scp079PlayerScript>();
			var levels = scp.levels;

			for (int i = 0; i < levels.Length; i++)
			{
				var level = levels[i];

				switch (i)
				{
					case 0:
						level.maxMana = 10;
						break;
					case 1:
						level.maxMana = 15;
						break;
					case 2:
						level.maxMana = 25;
						break;
					case 3:
						level.maxMana = 30;
						break;
					case 4:
						level.maxMana = 35;
						break;
				}
			}

			scp.NetworkmaxMana = 10;
		}

		/// <summary>
		/// true if checking on death event
		/// </summary>
		private void CheckEndPhase2(bool death)
		{
			int recount = death ? 1 : 0;

			//todo testing
			if (Round.Stats.SCPAlive == 0 + recount)
			{
				ClassDWin();
			}
			else
			if (Round.Stats.ClassDAlive == 0 + recount)
			{
				SCPWin();
			}
		}

		private const float GeneratorSpeechTime = 10; // 5 originally, 10 for last one

		private IEnumerator<float> Phase2()
		{
			// Wait for cassie to shut up
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			currentGameState = ZoneType.HCZ;

			var decon = cachedHost.GetComponent<DecontaminationLCZ>() as DecontaminationLCZ;
			// If decontaminate hasn't started
			if (decon.time < 704f)
			{
				// If decontamination is doing 30 sec countdown, wait for it to finish first
				if (decon.time >= 666)
				{
					yield return Timing.WaitForSeconds(704.4f - decon.time);
				}
				else
				{
					curAnmInfo.SetValue(decon, 5);
					decon.time = 704.4f;
				}
			}

			// Send nuts back home so they can't sneak in the elevators
			var nuts = Server.GetPlayers(Smod2.API.Team.SCP);
			var nutCount = nuts.Count;
			var doorPos = Tools.Vec3ToVec(cached173Door.localPos);

			for (int i = 0; i < nutCount; i++)
			{
				nuts[i].Teleport(doorPos);
			}

			yield return Timing.WaitForSeconds(2);

			// Run elevators
			foreach (var elevator in LczElevators)
			{
				elevator.MovingSpeed = 10;
				elevator.Locked = false;
				elevator.Use();
				elevator.Locked = true;
			}

			// Doors elevator close
			yield return Timing.WaitForSeconds(1);

			// Spawn survivors
			var players = Server.GetPlayers(Role.SPECTATOR);
			var playerCount = players.Count;

			const int ClassDHP = 100;
			var epCount = PlayersReachedElevator.Count;
			int hpEach = (int)((ClassDHP * epCount) / playerCount);
			Broadcast(5, $"Dead players have been respawned. All players now have {hpEach} HP.");
			hpEach = ClassDHP - hpEach;
			for (int i = 0; i < epCount; i++)
			{
				var ep = PlayersReachedElevator[i];
				var player = ep.player;

				player.ChangeRole(Role.CLASSD, true, false, false);

				// Position inside the elevator they clicked.
				player.Teleport(Tools.Vec3ToVec((ep.Elevator.GetComponent() as Lift).transform.position + new Vector3(0, 1.35f, 0)));

				// Equal HP
				player.Damage(hpEach);

				// Return Inventory
				player.GiveItem(ItemType.FLASHLIGHT);
				var inv = ep.Inventory;
				var invCount = ep.Inventory.Count;
				if (invCount > 0)
				{
					for (int j = 0; j < invCount; j++)
					{
						player.GiveItem(inv[j]);
					}
				}

				// Broadcast

				PersonalBroadcast(player, 10, "Activate the generators to make the exit open faster.\n Hint: Use your flashlight.");
			}

			for (int i = 0; i < playerCount; i++)
			{
				var player = players[i];

				if (player.TeamRole.Role != Role.SPECTATOR) continue;

				player.ChangeRole(Role.CLASSD, true, false, false);

				// Position inside random elevator
				var randomEv = LczElevators[UnityEngine.Random.Range(0, LczElevators.Count)];
				player.Teleport(Tools.Vec3ToVec((randomEv.GetComponent() as Lift).transform.position + new Vector3(0, 1.35f, 0)));

				// Equal HP
				player.Damage(hpEach);

				// Return Inventory
				player.GiveItem(ItemType.FLASHLIGHT);

				// Broadcast
				PersonalBroadcast(player, 10, "Activate the generators to make the exit open faster.\n Hint: Use your flashlight.");
			}

			// Open locked doors
			var doorCount = CachedDoors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedDoors[i];

				if (door.doorType == 2)
				{
					door.NetworkisOpen = true;
				}
			}

			// Wait enough time for decontamination message
			yield return Timing.WaitForSeconds(14);

			// Start lights out
			Server.Map.Shake();
			cachedGenerator.CallRpcOvercharge();
			flickerTimer = 10;
			var counterHandle = Timing.RunCoroutine(Counters());

			yield return Timing.WaitForSeconds(1);

			// Change SCP to doggo/Comp
			nuts = Server.GetPlayers(Smod2.API.Team.SCP);
			nutCount = nuts.Count;

			// Spawn SCP079 instead of doggo if 2 or more SCP present.
			if (nutCount > 1)
			{
				var nut = nuts[0];
				nut.ChangeRole(Role.SCP_079);

				PersonalBroadcast(nut, 10, "Help SCP 939 catch Class D, they can't see players that are sneaking around.\n" +
					"Press tab to open the map. You can close and lock doors on players.");
			}
			else if (nutCount == 1)
			{
				nuts[0].ChangeRole(UnityEngine.Random.Range(0f, 1f) > 0.5f ? Role.SCP_939_53 : Role.SCP_939_89);
			}

			for (int i = 1; i < nutCount; i++)
			{
				nuts[i].ChangeRole(UnityEngine.Random.Range(0f, 1f) > 0.5f ? Role.SCP_939_53 : Role.SCP_939_89);
			}

			Server.Map.AnnounceCustomMessage("WARNING . POWER FAILURE . . SCP 9 3 9 CONTAINMENT BREACH . . . CAUTION . THEY BYTE");

			yield return Timing.WaitForOneFrame;
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			// Start countdown
			escapeTimer = (int)(escapeTimerMinutes * 60);
			var announceLength = announcements.Length;
			var lastAnnouncement = announcements[announceLength - 1];
			UpdateCurrentAnnouncement(false);

			// if start time is 10 seconds over neck announcement, broadcast message
			var startAnnouncement = announcements[currentAnnouncement];
			if (announcements[currentAnnouncement].StartTime + 10 < escapeTimer)
			{
				Server.Map.AnnounceCustomMessage($"POWER REACTIVATION IN OVER {announcements[currentAnnouncement].Text}");
			}

			// Checking for countdown announcements/endings
			while (currentGameState == ZoneType.HCZ)
			{
				if (escapeTimer >= lastAnnouncement.StartTime)
				{
					var currAnnounce = announcements[currentAnnouncement];

					if (escapeTimer == currAnnounce.StartTime)
					{
						currentAnnouncement++;

						Server.Map.AnnounceCustomMessage($"POWER REACTIVATION IN {currAnnounce.Text}");
					}
				}
				else if (escapeTimer < 6)
				{
					if (escapeTimer > 0) yield return Timing.WaitForSeconds(escapeTimer);
					break;
				}
				else if (escapeTimer <= 13)
				{
					var countDownMessage = new StringBuilder(80);
					countDownMessage.Clear();
					countDownMessage.Append("POWER REACTIVATION IN ");

					for (int i = escapeTimer - 3; i >= 1; i--)
					{
						countDownMessage.Append(i);
						countDownMessage.Append(" . ");
					}

					Server.Map.AnnounceCustomMessage(countDownMessage.ToString());

					yield return Timing.WaitForSeconds(escapeTimer);
					break;
				}

				yield return Timing.WaitForSeconds(1);
			}

			Timing.KillCoroutines(counterHandle);
			if (flickerTimer > 2) yield return Timing.WaitForSeconds(flickerTimer - 2);

			Server.Map.AnnounceCustomMessage("EMERGENCY BACKUP POWER ENGAGED . ENTRANCE CHECKPOINT IS NOW OPEN");

			// Unlock entrance checkpoint
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedDoors[i];

				if (door.doorType == 3)
				{
					door.OpenDecontamination();
				}
			}

			Timing.RunCoroutine(Phase3());
		}

		/// <summary>
		/// Automatic flickering and countdown timer
		/// </summary>
		private IEnumerator<float> Counters()
		{
			while (currentGameState == ZoneType.HCZ)
			{
				yield return Timing.WaitForSeconds(1);

				escapeTimer--;
				flickerTimer--;

				if (flickerTimer == 0)
				{
					flickerTimer = 10;
					cachedGenerator.CallRpcOvercharge();
				}
			}
		}

		/// <summary>
		/// Updates the currentAnnouncement depending on the escapeTimer.
		/// </summary>
		/// <param name="generatorActivated">Will attempt to broadcast a message if there's enough time before the next announcement</param>
		private void UpdateCurrentAnnouncement(bool generatorActivated)
		{
			var announceLength = announcements.Length;

			if (escapeTimer <= announcements[announceLength - 1].StartTime) return;

			for (int i = 0; i < announceLength; i++)
			{
				var ananas = announcements[i];

				if (escapeTimer >= ananas.StartTime)
				{
					currentAnnouncement = i;

					if (generatorActivated && escapeTimer - ananas.StartTime > 10)
					{
						string customA = generatorCount < 5 ? $"SCP079RECON{generatorCount} . . " : string.Empty;
						var previousAn = announcements[currentAnnouncement - 1];

						if (currentAnnouncement - 1 < 0)
						{
							Server.Map.AnnounceCustomMessage($"{customA}POWER REACTIVATION IN OVER {ananas.Text}");
						}
						else
						{
							Server.Map.AnnounceCustomMessage($"{customA}POWER REACTIVATION IN LESS THEN {previousAn.Text}");
						}
					}

					return;
				}
			}
		}

		// Wait until last announcement finishes before doing new time logic.
		public void OnGeneratorFinish(GeneratorFinishEvent ev)
		{
			generatorCount++;

			genLocalTimeInfo.SetValue(ev.Generator.GetComponent() as Generator079, 1f);

			Timing.RunCoroutine(DelayedGen());
		}
		private IEnumerator<float> DelayedGen()
		{
			yield return Timing.WaitForOneFrame;
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);
			escapeTimer -= (int)(timeReductionPerGeneratorMinutes * 60);
			UpdateCurrentAnnouncement(true);
		}

		#endregion

		#region Phase 3

		private static readonly Vector pocketPos = Vector.Down * 1997f;

		public void On106CreatePortal(Player106CreatePortalEvent ev)
		{
			var spawns = ArithSpawningKit.RandomPlayerSpawning.RandomPlayerSpawning.Instance.Data.PlayerLoadedSpawns;
			ev.Position = spawns[UnityEngine.Random.Range(0, spawns.Count)].Position;
		}

		public void OnPocketDimensionDie(PlayerPocketDimensionDieEvent ev)
		{
			ev.Die = false;
			ev.Player.Teleport(pocketPos);
		}

		public void OnPocketDimensionExit(PlayerPocketDimensionExitEvent ev)
		{
			//todo ???
		}

		private IEnumerator<float> Phase3()
		{
			currentGameState = ZoneType.ENTRANCE;

			yield return Timing.WaitForOneFrame;
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			var map = Server.Map;
			//todo open_doors_on_countdown also, 120 max nuke time apparently.
			map.AnnounceCustomMessage("YOU . WILL . NOT . ESCAPE . . . . SCP 1 0 6 CONTAINMENT BREACH");

			yield return Timing.WaitForSeconds(6);

			foreach (var door in CachedExplosiveDoors)
			{
				door.DestroyDoor(true);
			}
			cachedGenerator.CallRpcOvercharge();
			map.Shake();

			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			// Respawn players, pool HP
			var players = Server.GetPlayers(Smod2.API.Team.CLASSD);
			var playerCount = players.Count;
			var hpPool = 0;
			for (int i = 0; i < playerCount; i++)
			{
				var player = players[i];

				if (player.TeamRole.Role == Role.CLASSD)
				{
					hpPool += player.GetHealth();
				}
				else
				{
					player.ChangeRole(Role.CLASSD);
				}

				PersonalBroadcast(player, 10, "Larry has trapped you in the Pocket Dimension!\nEscape as soon as possible");
			}

			// Equal HP and Teleport
			var targetHP = hpPool / playerCount;
			Broadcast(5, $"Dead players have been respawned. All players now have {targetHP} HP.");
			for (int i = 0; i < playerCount; i++)
			{
				var player = players[i];

				player.SetHealth(targetHP, DamageType.NONE);
				player.Teleport(pocketPos);
			}

			yield return Timing.WaitForSeconds(15);

			map.AnnounceCustomMessage("SCP 0 9 6 CONTAINMENT BREACH . . YOU WILL C IT AND BE TERMINATED");

			//todo spawn SCP, start Larry countdown

		}

		/// <summary>
		/// Force Larry to teleport every so often
		//todo fix this up, make the timer reset every jump
		/// </summary>
		/// <returns></returns>
		//private IEnumerator<float> TeleportLarry()
		//{
		//	//var scp = (player.GetGameObject() as GameObject).GetComponent<Scp106PlayerScript>();

		//if (scp.goingViaThePortal)
		//{

		//}
		//scp.CallCmdMakePortal();
		//yield return Timing.WaitForSeconds(1f);
		//scp.CallCmdUsePortal();
		//}

		public void OnCheckEscape(PlayerCheckEscapeEvent ev) => ClassDWin();

		private void SCPWin()
		{
			Timing.RunCoroutine(EndGame("SCP Win!"));
		}

		private void ClassDWin()
		{
			Timing.RunCoroutine(EndGame("Class D Win!"));
		}

		private IEnumerator<float> EndGame(string winMessage)
		{
			currentGameState = ZoneType.UNDEFINED;

			Broadcast((uint)endScreenSpeed, winMessage);

			yield return Timing.WaitForSeconds(endScreenSpeed);

			Round.RestartRound();
		}

		#endregion

		#region Custom Broadcasts

		private void PersonalClearBroadcasts(Player player)
		{
			var connection = cachedPlayerConnFieldInfo.GetValue(player) as NetworkConnection;

			if (connection == null)
			{
				return;
			}

			cachedBroadcast.CallTargetClearElements(connection);
		}

		private void PersonalBroadcast(Player player, uint duration, string message)
		{
			var connection = cachedPlayerConnFieldInfo.GetValue(player) as NetworkConnection;

			if (connection == null)
			{
				return;
			}

			cachedBroadcast.CallTargetAddElement(connection, message, duration, false);
		}

		private void ClearBroadcasts() => cachedBroadcast.CallRpcClearElements();

		private void Broadcast(uint duration, string message)
		{
			cachedBroadcast.CallRpcAddElement(message, duration, false);
		}

		public void On079Door(Player079DoorEvent ev)
		{
			var doorType = (ev.Door.GetComponent() as Door).doorType;

			if (doorType == 3)
			{
				ev.Allow = false;
			}
		}

		public void OnStopCountdown(WarheadStopEvent ev) => ev.Cancel = false;


		#endregion
	}
}