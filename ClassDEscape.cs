using System.Collections.Generic;
using System.Reflection;
using Smod2;
using Smod2.API;
using Smod2.Config;
using Smod2.Attributes;
using Smod2.Events;
using UnityEngine;
using Smod2.EventHandlers;
using MEC;
using ArithFeather.ArithSpawningKit.SpawnPointTools;
using System.Runtime.CompilerServices;
using System.Text;
using System;

/// <summary>
/// 
/// -todo-
/// remove ammo packs on death
/// Less final ending. Give scores "You killed this many""This many escaped" "MVP" "MOST KILLS" SURVIVED LONGEST" "ESCAPED" etc.
/// 
/// fix players not spawning in elevator
/// fix SCP106;
/// Change scp 079 to shy guy
/// 
/// -low priority-
/// Make 914 useful.
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
		IEventHandlerPlayerJoin, IEventHandlerElevatorUse, IEventHandlerDoorAccess, IEventHandlerRoundRestart,
		IEventHandlerCheckEscape, IEventHandlerRoundStart, IEventHandlerCallCommand, IEventHandler079AddExp,
		IEventHandlerGeneratorFinish, IEventHandlerPlayerDie, IEventHandlerSCP914ChangeKnob, IEventHandlerLateDisconnect,
		IEventHandlerPlayerHurt, IEventHandlerLCZDecontaminate, IEventHandler079Door, IEventHandlerWarheadChangeLever,
		IEventHandlerWarheadStopCountdown, IEventHandlerPocketDimensionDie, IEventHandler106CreatePortal, 
		IEventHandler106Teleport
	{
		public const string ModVersion = "1.0";

		#region Text values

		private const string ServerHighLightColor = "#38a8b5";

		private const string ServerInfoColor = "#23eb44";
		private const int ServerInfoSize = 50;

		private const string ClassInfoColor = "#23eb44";
		private const int ClassInfoSize = 50;

		private const string WinColor = "#b81111";
		private const int WinTextSize = 70;

		private static readonly string JoinGameMessage1 =
			$"<size={ServerInfoSize}><color={ServerInfoColor}>Welcome to <color={ServerHighLightColor}>Class D Escape v{ModVersion}!</color> Press ` to open the console and enter '<color={ServerHighLightColor}>.help</color>' for mod information!</color></size>";
		private static readonly string JoinGameMessage2 =
			$"<size={ServerInfoSize}><color={ServerInfoColor}>If you like the plugin, join the discord for updates!\n <color={ServerHighLightColor}>https://discord.gg/DunUU82</color></color></size>";

		private static readonly string HelpMessage = "Git Gud";

		private static readonly string AttemptNukeMessage = "OKAY THEN";

		#endregion

		[ConfigOption] private readonly bool disablePlugin = false;
		[ConfigOption] private readonly bool showPlayerJoinMessage = true;
		[ConfigOption] private readonly float expMultiplier = 50f;
		[ConfigOption] private readonly float peanutStartHP = 1f;
		[ConfigOption] private readonly int endScreenSpeedSeconds = 15;
		[ConfigOption] private readonly float escapeTimerMinutes = 15f;
		[ConfigOption] private readonly float timeReductionPerGeneratorMinutes = 3f;
		[ConfigOption] private readonly bool skipIntro = false;
		[ConfigOption] private readonly float lastPhaseNukeTimerMinutes = 5;
		[ConfigOption] private readonly int secondsUntil106Teleport = 30;
		[ConfigOption] private readonly bool allow106Teleport = true;

		// Called once on register
		private bool useDefaultConfig; // manually get on register

		private float timeUntil173;
		private float deconTime;

		//++ Round Data

		private ZoneType currentGameState;
		private bool roundStarted = false;
		private bool endingGame = false;
		private bool isIntermission;

		private Broadcast cachedBroadcast;
		private List<Door> cachedDoors;
		private List<Door> CachedDoors => cachedDoors ?? (cachedDoors = new List<Door>(200));
		private GameObject cachedHost;
		private Room[] cachedRooms;

		private List<Player> roundSCP;
		private List<Player> RoundSCP => roundSCP ?? (roundSCP = new List<Player>(3));
		private List<Player> roundPlayers;
		private List<Player> RoundPlayers => roundPlayers ?? (roundPlayers = new List<Player>(17));

		private List<CoroutineHandle> activateCoHandles;
		private List<CoroutineHandle> ActivateCoHandles => activateCoHandles ?? (activateCoHandles = new List<CoroutineHandle>(10));

		// Doors
		private List<Door> cachedPrisonDoors;
		private List<Door> CachedPrisonDoors => cachedPrisonDoors ?? (cachedPrisonDoors = new List<Door>(30));
		private List<Door> cachedLczDoors;
		private List<Door> CachedLczDoors => cachedLczDoors ?? (cachedLczDoors = new List<Door>(70));
		private List<Door> cachedHczDoors;
		private List<Door> CachedHczDoors => cachedHczDoors ?? (cachedHczDoors = new List<Door>(70));
		private Door cached173Door;

		//+ Phase 1

		private List<ElevatorPlayer> playersReachedElevator;
		private List<ElevatorPlayer> PlayersReachedElevator => playersReachedElevator ?? (playersReachedElevator = new List<ElevatorPlayer>(14));
		private List<Elevator> lczElevators;
		private List<Elevator> LczElevators => lczElevators ?? (lczElevators = new List<Elevator>(4));

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
		private bool attemptedToNuke;


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

		private bool allowPortal;
		private bool autoTeleport;
		private List<Elevator> hczElevators;
		private List<Elevator> HczElevators => hczElevators ?? (hczElevators = new List<Elevator>(2));

		private List<Player> escapedPlayers;
		private List<Player> EscapedPlayers => escapedPlayers ?? (escapedPlayers = new List<Player>(17));

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
				case "warhead_tminus_start_duration":
					ev.Value = 120;
					break;

				// Editable configs
				case "disable_decontamination": //todo false
					if (useDefaultConfig) ev.Value = true;
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
					if (useDefaultConfig) ev.Value = "40044444444404444444";
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
			isIntermission = false;

			RoundSCP.Clear();
			RoundPlayers.Clear();

			RandomItemSpawner.RandomItemSpawner.Instance.UseDefaultEvents = false;

			cachedRooms = Server.Map.Get079InteractionRooms(Scp079InteractionType.CAMERA);
			CacheDoors();

			// Phase 1
			currentGameState = ZoneType.LCZ;

			PlayersReachedElevator.Clear();
			Scp914.singleton.working = true;
			LczElevators.Clear();
			HczElevators.Clear();

			var elevators = Server.Map.GetElevators();
			foreach (var elevator in elevators)
			{
				switch (elevator.ElevatorType)
				{
					case ElevatorType.LiftA:
					case ElevatorType.LiftB:
						LczElevators.Add(elevator);
						break;

					case ElevatorType.GateA:
						break;
					case ElevatorType.GateB:
						break;

					case ElevatorType.WarheadRoom:
					case ElevatorType.SCP049Chamber:
						HczElevators.Add(elevator);
						break;
				}
			}

			// Phase 2
			cachedGenerator = Server.Map.GetGenerators()[0].GetComponent() as Generator079;
			generatorCount = 0;
			attemptedToNuke = false;

			// Phase 3
			EscapedPlayers.Clear();
			allowPortal = false;
			autoTeleport = false;

			GC.Collect(3);
		}

		/// <summary>
		/// Also locks Class D and SCP 173 Doors
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void CacheDoors()
		{
			CachedDoors.Clear();
			CachedHczDoors.Clear();
			CachedLczDoors.Clear();
			CachedPrisonDoors.Clear();

			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			bool found173Door = false;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;

				Scp079Interactable component = door.GetComponent<Scp079Interactable>();
				if (component != null)
				{
					Scp079Interactable.ZoneAndRoom zoneAndRoom = component.currentZonesAndRooms[0];
					switch (zoneAndRoom.currentZone)
					{
						case "HeavyRooms":

							CachedHczDoors.Add(door);

							//if (door.doorType == 0)
							//{
							//	Scp079Interactable component = door.GetComponent<Scp079Interactable>();
							//	if (component != null)
							//	{
							//		Scp079Interactable.ZoneAndRoom zoneAndRoom = component.currentZonesAndRooms[0];
							//		if (zoneAndRoom.currentZone == "HeavyRooms")
							//		{
							//			CachedExplosiveDoors.Add(door);
							//		}
							//	}
							//}

							break;

						case "LightRooms":

							CachedLczDoors.Add(door);

							// Cache prison doors and SCP173 door
							if (door.doorType == 0)
							{
								if (door.name.StartsWith("PrisonDoor"))
								{
									door.Networklocked = true;
									CachedPrisonDoors.Add(door);
								}
								else if (!found173Door && door.transform.parent.name == "MeshDoor173")
								{
									found173Door = true;
									cached173Door = door;
									door.Networklocked = true;
								}
							}

							break;
							//case "EntranceRooms":
							//	break;
					}
				}

				CachedDoors.Add(door);
			}
		}

		public void OnRoundStart(RoundStartEvent ev)
		{
			if (endingGame) return;
			roundStarted = true;

			// Cache players/SCP
			var players = Server.GetPlayers();
			foreach (var p in players)
			{
				if (p.TeamRole.Team == Smod2.API.Team.SCP)
				{
					RoundSCP.Add(p);
				}
				else
				{
					RoundPlayers.Add(p);
				}
			}

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
			itemSpawner.SpawnItems(2, ZoneType.LCZ, ItemType.ZONE_MANAGER_KEYCARD);
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
			itemSpawner.SpawnItems(2, ZoneType.ENTRANCE, ItemType.FACILITY_MANAGER_KEYCARD);
			itemSpawner.SpawnItems((int)(averageSpawnCount * 1.5f), ZoneType.ENTRANCE, ItemType.MEDKIT);


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


			//ActivateCoHandles.Add(Timing.RunCoroutine(RoundStart()));

			//todo testing below - instant start phase 2

			//var p = Server.GetPlayers(Role.CLASSD)[0];
			//OnPlayerJoin(new PlayerJoinEvent(p));
			//p.ChangeRole(Role.SPECTATOR, false, false, false);
			//CheckEndPhase1(0);

			//todo testing phase 3
			//Timing.RunCoroutine(Phase3());
		}

		#endregion

		#region Shared events

		public void OnPlayerJoin(PlayerJoinEvent ev)
		{
			if (endingGame) return;

			var player = ev.Player;

			if (showPlayerJoinMessage)
			{
				try
				{
					PersonalBroadcast(ev.Player, 8, JoinGameMessage1);
					PersonalBroadcast(ev.Player, 8, JoinGameMessage2);
				}
				catch
				{
					Info("Null ref on joining player. Restarting Server...");
				}
			}

			if (roundStarted)
			{
				RoundPlayers.Add(player);
			}
		}

		/// <summary>
		/// Update player lists, rechecking ending
		/// </summary>
		public void OnLateDisconnect(LateDisconnectEvent ev)
		{
			if (endingGame || !roundStarted) return;

			Timing.RunCoroutine(_delayedDisconnect());
		}

		private IEnumerator<float> _delayedDisconnect()
		{
			yield return Timing.WaitForOneFrame;

			var currentPlayers = Server.GetPlayers();
			var currentPlayerCount = currentPlayers.Count;
			var previousPlayerCount = currentPlayerCount + 1;

			//Get previous players
			var previousPlayers = new List<Player>(previousPlayerCount);
			previousPlayers.AddRange(RoundPlayers);
			previousPlayers.AddRange(RoundSCP);

			//Remove all matches to current players
			for (int i = currentPlayerCount; i >= 0; i--)
			{
				var ppID = previousPlayers[i].PlayerId;

				for (int j = 0; j < currentPlayerCount; j++)
				{
					if (ppID == currentPlayers[j].PlayerId)
					{
						previousPlayers.RemoveAt(i);
						break;
					}
				}
			}

			if (previousPlayers.Count != 1)
			{
				Error("Could not find disconnected player");
				yield break;
			}

			Player disconnectedPlayer = previousPlayers[0];
			Info($"{disconnectedPlayer.Name} has disconnected!");


			//Remove cached disconnected player
			bool skipSecondCheck = false;
			for (int i = RoundSCP.Count - 1; i >= 0; i--)
			{
				var player = RoundSCP[i];

				if (player.PlayerId == disconnectedPlayer.PlayerId)
				{
					RoundSCP.RemoveAt(i);
					skipSecondCheck = true;
					break;
				}
			}
			if (!skipSecondCheck)
			{
				for (int i = RoundPlayers.Count - 1; i >= 0; i--)
				{
					var player = RoundPlayers[i];

					if (player.PlayerId == disconnectedPlayer.PlayerId)
					{
						RoundPlayers.RemoveAt(i);
						break;
					}
				}
			}

			// Check round end on no SCP/Players left
			if (RoundSCP.Count == 0)
			{
				EndGame("SCP disconnected. There are no SCP left, restarting round...");
			}
			else if (RoundPlayers.Count == 0)
			{
				EndGame("Player disconnected. There are no players left, restarting round...");
			}

			//Update lists
			switch (currentGameState)
			{
				case ZoneType.UNDEFINED:
					break;

				case ZoneType.LCZ:

					RemoveElevatorPlayer(disconnectedPlayer.PlayerId);

					CheckEndPhase1(false);

					break;

				case ZoneType.HCZ:

					RemoveElevatorPlayer(disconnectedPlayer.PlayerId); // Check here incase

					CheckEndPhase2(false, false);

					break;
				case ZoneType.ENTRANCE:

					//Remove escaped player
					var escapedPlayerCount = EscapedPlayers.Count;
					for (int i = 0; i < escapedPlayerCount; i++)
					{
						if (EscapedPlayers[i].PlayerId == disconnectedPlayer.PlayerId)
						{
							EscapedPlayers.RemoveAt(i);
							break;
						}
					}

					CheckEndPhase3(false, false);

					break;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void RemoveElevatorPlayer(int playerID)
		{
			var playerCount = PlayersReachedElevator.Count;
			for (int i = PlayersReachedElevator.Count - 1; i >= 0; i--)
			{
				var player = PlayersReachedElevator[i].player;

				if (player.PlayerId == playerID)
				{
					PlayersReachedElevator.RemoveAt(i);
					return;
				}
			}
		}

		public void OnCallCommand(PlayerCallCommandEvent ev)
		{
			// Testing Announcements
			//if (ev.Player.GetRankName().ToUpper() == "OWNER")
			//	Server.Map.AnnounceCustomMessage(ev.Command);

			switch (ev.Command.ToUpper())
			{
				case "HELP":
					ev.ReturnMessage = HelpMessage;
					break;
			}

			ActivateCoHandles.Add(Timing.RunCoroutine(LarryTimer(ev.Player)));

			if (ev.Command.ToUpper() == "T")
			{
				Timing.RunCoroutine(_escapeWin());
			}
		}

		public void OnPlayerDie(PlayerDeathEvent ev)
		{
			// Stupid server death
			if (endingGame || ev.Killer.TeamRole.Team == Smod2.API.Team.NONE) return;

			if (roundStarted)
			{
				var player = ev.Player;

				switch (currentGameState)
				{
					case ZoneType.LCZ:

						if (player.TeamRole.Team != Smod2.API.Team.SCP)
						{
							PersonalBroadcast(player, 5, "You will respawn if Class D Makes it to phase 2...");
							CheckEndPhase1(true);
						}

						break;

					case ZoneType.HCZ:

						if (player.TeamRole.Team != Smod2.API.Team.SCP)
						{
							PersonalBroadcast(player, 5, "You will respawn if Class D Makes it to phase 3...");
							CheckEndPhase2(true, false);
						}
						else
						{
							CheckEndPhase2(false, true);
						}

						break;

					case ZoneType.ENTRANCE:

						if (player.TeamRole.Team != Smod2.API.Team.SCP)
						{
							PersonalBroadcast(player, 5, "You were unable to escape! Please wait for the next round.");
							CheckEndPhase2(true, false);
						}
						else
						{
							CheckEndPhase2(false, true);
						}

						break;
				}
			}
		}

		public void OnSetRole(PlayerSetRoleEvent ev)
		{
			if (endingGame || ev.Role == Role.SPECTATOR) return;

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

					if (ev.Role == Role.SCP_106)
					{
						ActivateCoHandles.Add(Timing.RunCoroutine(LarryTimer(ev.Player)));
					}

					break;

				case ZoneType.ENTRANCE:

					//Set SCP079 stats
					if (ev.Role == Role.SCP_079)
					{
						Timing.RunCoroutine(SetSCP079Stats(ev.Player));
					}

					break;
			}
		}

		public void OnElevatorUse(PlayerElevatorUseEvent ev)
		{
			if (endingGame) return;

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
						PersonalBroadcast(player, 5, "You have escaped to heavy containment! Please wait for the other players...");

						CheckEndPhase1(false);
					}

					break;

				case ZoneType.HCZ:


					break;

				case ZoneType.ENTRANCE:
					break;
			}
		}

		/// <summary>
		/// Doortypes:
		/// 0 - normal door
		/// 1 - Maybe 
		/// 2 - Card-Required door (other than checkpoint)
		/// 3 - Checkpoint
		/// </summary>
		/// <param name="ev"></param>
		public void OnDoorAccess(PlayerDoorAccessEvent ev)
		{
			if (endingGame) return;

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

						var minutes = Mathf.FloorToInt(escapeTimer / 60);
						var seconds = escapeTimer - (minutes * 60);
						PersonalBroadcast(ev.Player, 3, $"Power is down for {minutes}:{seconds}");
					}

					break;

				case ZoneType.ENTRANCE:
					break;
			}

			// For testing
			var door = (ev.Door.GetComponent() as Door);
			Info(door.DoorName);
			Info(door.doorType.ToString());
			Info(door.name);
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
					else if (ev.DamageType == DamageType.SCP_939)
					{
						ev.Damage /= 2;
					}

					break;

				case ZoneType.ENTRANCE:

					// Make sure pocket can't kill you
					if (ev.DamageType == DamageType.POCKET)
					{
						var playerHP = ev.Player.GetHealth();
						if (playerHP - ev.Damage < 1)
						{
							ev.Damage = playerHP - 1;
						}
					}
					else if (ev.DamageType == DamageType.SCP_106)
					{
						ev.Damage = 0;
					}

					break;
			}
		}

		private void SCPWin() => EndGame("SCP Win!");

		private void ClassDWin() => EndGame("Class D Win!");

		private CoroutineHandle endingCoRo;

		private void EndGame(string winMessage) => endingCoRo = Timing.RunCoroutine(_endGame(winMessage));
		private IEnumerator<float> _endGame(string winMessage)
		{
			foreach (var coRo in ActivateCoHandles)
			{
				Timing.KillCoroutines(coRo);
			}
			ActivateCoHandles.Clear();

			endingGame = true;
			Broadcast((uint)endScreenSpeedSeconds, winMessage);

			yield return Timing.WaitForSeconds(endScreenSpeedSeconds);

			Round.RestartRound();
		}

		public void OnRoundRestart(RoundRestartEvent ev)
		{
			endingGame = false;
			roundStarted = false;
			currentGameState = ZoneType.UNDEFINED;

			Timing.KillCoroutines(endingCoRo);
			foreach (var coRo in ActivateCoHandles)
			{
				Timing.KillCoroutines(coRo);
			}
			ActivateCoHandles.Clear();
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

		public void OnSCP914ChangeKnob(PlayerSCP914ChangeKnobEvent ev) => ev.KnobSetting = KnobSetting.ROUGH;

		public void OnDecontaminate()
		{
			if (endingGame) return;

			if (currentGameState == ZoneType.LCZ) ActivateCoHandles.Add(Timing.RunCoroutine(KillSCPDecontamination()));
		}

		private const int KillPeanutInSeconds = 10;
		private const int PeanutHP = 3200;
		private int peanutDeconDamage;

		private IEnumerator<float> KillSCPDecontamination()
		{
			peanutDeconDamage = (int)(((PeanutHP * peanutStartHP) / KillPeanutInSeconds) / 4f);

			ActivateCoHandles.Add(Timing.RunCoroutine(DecontaminationPlayerDamage()));

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
		/// <param name="death">Death event hasn't updated a dead player</param>
		private void CheckEndPhase1(bool death)
		{
			if (isIntermission || endingGame) return;

			var classDAlive = death ? Round.Stats.ClassDAlive - 1 : Round.Stats.ClassDAlive;

			Info(classDAlive.ToString()); //todo remove if working
			if (classDAlive == 0)
			{
				if (PlayersReachedElevator.Count == 0)
				{
					EndGame("SCP Win!");
				}
				else
				{
					ActivateCoHandles.Add(Timing.RunCoroutine(Phase2()));
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

				yield return Timing.WaitForSeconds(3);
				yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);
			}

			StartGameClassD();

			yield return Timing.WaitForSeconds(timeUntil173);

			StartGameSCP();

			Server.Map.AnnounceCustomMessage("DANGER . SCP 1 7 3 CONTAINMENT BREACH");
			yield return Timing.WaitForSeconds(3);
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			StartDecontamination();

			yield return Timing.WaitForSeconds(TimeUntilIntroduction);

			Server.Map.AnnounceCustomMessage(lczCassieMessages[UnityEngine.Random.Range(0, lczCassieMessages.Length)]);
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
			doorCount = CachedLczDoors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedLczDoors[i];

				if (door.doorType == 2)
				{
					door.NetworkisOpen = true;
				}
			}

			// Send broadcasts and lower SCP hp
			var playerCount = RoundPlayers.Count;
			for (int i = 0; i < playerCount; i++)
			{
				var player = RoundPlayers[i];
				PersonalBroadcast(player, 10, "Work together to escape light containment via an elevator.");
			}

			int scpDamage = (int)(PeanutHP * (1 - peanutStartHP));
			if (scpDamage > 0)
			{
				playerCount = RoundSCP.Count;
				for (int i = 0; i < playerCount; i++)
				{
					var player = RoundSCP[i];
					PersonalBroadcast(player, 10, "Your health has been lowered to increase your speed.");
					player.Damage(scpDamage, DamageType.NONE);
				}
			}
		}

		/// <summary>
		/// Called when SCP173 can escape
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void StartGameSCP()
		{
			// Flicker lights
			foreach (var item in cachedRooms)
			{
				if (item.ZoneType == ZoneType.LCZ)
				{
					item.FlickerLights();
				}
			}

			// Open SCP door
			cached173Door.Networklocked = false;
			cached173Door.NetworkisOpen = true;
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

		public void On079AddExp(Player079AddExpEvent ev) => ev.ExpToAdd *= expMultiplier;

		public void On079Door(Player079DoorEvent ev)
		{
			var doorType = (ev.Door.GetComponent() as Door).doorType;

			if (doorType == 3)
			{
				ev.Allow = false;
			}
		}

		private IEnumerator<float> SetSCP079Stats(Player player)
		{
			yield return Timing.WaitForSeconds(1);

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
		/// Check when:
		/// Player die, disconnect
		/// </summary>
		/// <param name="death">Death event hasn't updated a dead player</param>
		private void CheckEndPhase2(bool playerDeath, bool scpDeath)
		{
			if (isIntermission || endingGame) return;

			var classDAlive = playerDeath ? Round.Stats.ClassDAlive - 1 : Round.Stats.ClassDAlive;
			var scpAlive = scpDeath ? Round.Stats.SCPAlive - 1 : Round.Stats.SCPAlive;

			if (classDAlive == 0)
			{
				EndGame("SCP Win!");
			}
			else if (scpAlive == 0)
			{
				EndGame("Players Win!");
			}
		}

		private IEnumerator<float> Phase2()
		{
			isIntermission = true;
			currentGameState = ZoneType.HCZ;

			// Wait for cassie to shut up
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			// If decontaminate hasn't started
			var decon = cachedHost.GetComponent<DecontaminationLCZ>() as DecontaminationLCZ;
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
			var nutCount = RoundSCP.Count;
			var scpPos = Server.Map.GetSpawnPoints(Role.SCP_173)[0];

			for (int i = 0; i < nutCount; i++)
			{
				RoundSCP[i].Teleport(scpPos);
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
			const int ClassDHP = 100;
			var playerCount = RoundPlayers.Count;
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

			yield return Timing.WaitForOneFrame;

			for (int i = 0; i < playerCount; i++)
			{
				var player = RoundPlayers[i];

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
			var doorCount = CachedHczDoors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedHczDoors[i];

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
			ActivateCoHandles.Add(counterHandle);

			yield return Timing.WaitForSeconds(1);

			// Respawn SCP
			var countDownMessage = new StringBuilder(80);
			countDownMessage.Append("DANGER . POWER FAILURE . . SCP 1 0 6");
			var scpCount = RoundSCP.Count;

			if (scpCount >= 1)
			{
				var scp = RoundSCP[0];
				scp.ChangeRole(Role.SCP_106);
				scp.Teleport(GetRandomHeavyPos());
			}
			if (scpCount >= 2)
			{
				RoundSCP[1].ChangeRole(Role.SCP_096);
				countDownMessage.Append(" . AND SCP 0 9 6");
			}
			if (scpCount >= 3)
			{
				for (int i = 2; i < scpCount; i++)
				{
					RoundSCP[i].ChangeRole(UnityEngine.Random.Range(0f, 1f) > 0.5f ? Role.SCP_939_53 : Role.SCP_939_89);
				}
			}

			countDownMessage.Append(" CONTAINMENT BREACH");
			Server.Map.AnnounceCustomMessage(countDownMessage.ToString());

			isIntermission = false;
			CheckEndPhase2(false, false);

			yield return Timing.WaitForSeconds(3);
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

			ActivateCoHandles.Add(Timing.RunCoroutine(Phase3()));
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
			if (endingGame || currentGameState != ZoneType.HCZ) return;

			generatorCount++;

			genLocalTimeInfo.SetValue(ev.Generator.GetComponent() as Generator079, 1f);

			Timing.RunCoroutine(DelayedGen());
		}
		private IEnumerator<float> DelayedGen()
		{
			yield return Timing.WaitForSeconds(0.5f);
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);
			escapeTimer -= (int)(timeReductionPerGeneratorMinutes * 60);
			UpdateCurrentAnnouncement(true);
		}

		// Trying to nuke
		public void OnChangeLever(WarheadChangeLeverEvent ev)
		{
			ev.Allow = false;

			if (endingGame) return;

			if (!attemptedToNuke)
			{
				attemptedToNuke = true;
				ActivateCoHandles.Add(Timing.RunCoroutine(NukeAttempt()));
			}
		}
		private IEnumerator<float> NukeAttempt()
		{
			// Wait for important shit, pause half a second to let other code run and reannounce stuff.
			do
			{
				yield return Timing.WaitForSeconds(0.5f);
			}
			while (!NineTailedFoxAnnouncer.singleton.isFree);

			Server.Map.AnnounceCustomMessage(AttemptNukeMessage);
		}

		#endregion

		#region Phase 3

		private static readonly Vector pocketPos = Vector.Down * 1997f;

		public void OnStopCountdown(WarheadStopEvent ev) => ev.Cancel = false;

		/// <summary>
		/// Check when:
		/// Player escape, player die
		/// </summary>
		/// <param name="death">Death event hasn't updated a dead player</param>
		private void CheckEndPhase3(bool playerDeath, bool scpDeath)
		{
			if (isIntermission || endingGame) return;

			var classDAlive = playerDeath ? Round.Stats.ClassDAlive - 1 : Round.Stats.ClassDAlive;
			var scpAlive = scpDeath ? Round.Stats.SCPAlive - 1 : Round.Stats.SCPAlive;

			if (classDAlive == 0)
			{
				if (EscapedPlayers.Count > 0)
				{
					// Player Win
				}
				else
				{
					SCPWin();
				}
			}
			else if (scpAlive == 0)
			{
				ClassDWin();
			}
		}

		private IEnumerator<float> _escapeWin()
		{
			foreach (var coRo in ActivateCoHandles)
			{
				Timing.KillCoroutines(coRo);
			}
			ActivateCoHandles.Clear();
			endingGame = true;

			const int MaxPlayersToList = 5;
			var messages = new StringBuilder(100);
			messages.Append("Players Escaped: ");
			var escapeCount = EscapedPlayers.Count;
			var maxDisplay = Mathf.Min(MaxPlayersToList, escapeCount);
			for (int i = 0; i < maxDisplay; i++)
			{
				messages.Append(EscapedPlayers[i].Name);
				if (i < escapeCount - 1) messages.Append(", ");
				else if (escapeCount > MaxPlayersToList)
				{
					messages.Append($", and {escapeCount - i} more...");
				}
			}

			Broadcast(5, messages.ToString());
			messages.Clear();

			var sum = RoundSummary.singleton;

			var list = new RoundSummary.SumInfo_ClassList();
			list.time = RoundSummary.roundTime;
			list.class_ds = escapeCount;
			sum.CallRpcShowRoundSummary(sum.GetStartClassList(), list,
				RoundSummary.LeadingTeam.ChaosInsurgency,
				escapeCount, 0, RoundSummary.kills_by_scp, endScreenSpeedSeconds);

			yield return Timing.WaitForSeconds((float)(endScreenSpeedSeconds - 1));

			sum.CallRpcDimScreen();

			yield return Timing.WaitForSeconds(1f);

			Round.RestartRound();

			//string bestSCP;
			//int killCount;
			//var scpCount = RoundSCP.Count;
			//for (int i = 0; i < scpCount; i++)
			//{
			//	var scp = RoundSCP[i];
			//	var kills = scp.

			//	if (
			//}


			//Broadcast((uint)endScreenSpeedSeconds, winMessage);

			//yield return Timing.WaitForSeconds(endScreenSpeedSeconds);
			//yield return Timing.WaitUntilTrue(cachedBroadc)

			Round.RestartRound();
		}

		public void On106CreatePortal(Player106CreatePortalEvent ev)
		{
			if (allowPortal)
			{
				ev.Position = GetRandomHeavyPos();
			}
			else
			{
				ev.Position = null;
			}
		}

		public void On106Teleport(Player106TeleportEvent ev)
		{
			if (autoTeleport || allow106Teleport)
			{
				autoTeleport = false;
				Timing.RunCoroutine(_TeleportLarry(ev.Player));
			}
			else
			{
				ev.Position = null;
			}
		}

		private IEnumerator<float> _TeleportLarry(Player player)
		{
			var script = (player.GetGameObject() as GameObject).GetComponent<Scp106PlayerScript>();

			PersonalBroadcast(player, 2, $"Teleporting...");

			yield return Timing.WaitForSeconds(1);
			yield return Timing.WaitUntilFalse(() => script.goingViaThePortal);

			allowPortal = true;
			script.CallCmdMakePortal();
			yield return Timing.WaitForSeconds(0.5f);
			allowPortal = false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private Vector GetRandomHeavyPos()
		{
			var spawns = ArithSpawningKit.RandomPlayerSpawning.RandomPlayerSpawning.Instance.Data.PlayerLoadedSpawns;
			return spawns[UnityEngine.Random.Range(0, spawns.Count)].Position;
		}

		public void OnPocketDimensionDie(PlayerPocketDimensionDieEvent ev)
		{
			ev.Die = false;
			ev.Player.Teleport(pocketPos);
		}

		private IEnumerator<float> Phase3()
		{
			currentGameState = ZoneType.ENTRANCE;
			isIntermission = true;

			var map = Server.Map;

			Server.Map.AnnounceCustomMessage("EMERGENCY BACKUP POWER ENGAGED . ENTRANCE CHECKPOINT IS NOW OPEN");

			// Send elevators up
			foreach (var ele in HczElevators)
			{
				if (ele.ElevatorStatus == ElevatorStatus.Down)
				{
					ele.MovingSpeed = 3;
					ele.Use();
				}
			}

			yield return Timing.WaitForSeconds(6f);

			// Unlock entrance checkpoint
			var doorCount = CachedHczDoors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedHczDoors[i];

				if (door.doorType == 3)
				{
					door.OpenDecontamination();
				}
			}

			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			Server.Map.FemurBreaker(true);

			yield return Timing.WaitForSeconds(2f);

			// Send elevators up
			foreach (var ele in HczElevators)
			{
				if (ele.ElevatorStatus == ElevatorStatus.Down)
				{
					ele.MovingSpeed = 5;
					ele.Use();
				}
			}

			yield return Timing.WaitForSeconds(1);

			// Rid of SCP
			foreach (var scp in RoundSCP)
			{
				scp.ChangeRole(Role.SPECTATOR);
			}

			// Respawn players, pool HP
			var playerCount = roundPlayers.Count;
			var hpPool = 0;
			for (int i = 0; i < playerCount; i++)
			{
				var player = RoundPlayers[i];

				if (player.TeamRole.Role == Role.CLASSD)
				{
					hpPool += player.GetHealth();
				}
			}

			const int ClassDHP = 100;
			int hpEach = (int)(hpPool / playerCount);
			Broadcast(5, $"Dead players have been respawned with {hpEach} HP.");
			hpEach = ClassDHP - hpEach;

			// Damage players
			for (int i = 0; i < playerCount; i++)
			{
				var player = RoundPlayers[i];

				if (player.TeamRole.Role != Role.CLASSD)
				{
					player.ChangeRole(Role.CLASSD, true, false);

					// Position inside random elevator
					var randomEv = LczElevators[UnityEngine.Random.Range(0, LczElevators.Count)];
					player.Teleport(Tools.Vec3ToVec((randomEv.GetComponent() as Lift).transform.position + new Vector3(0, 1.35f, 0)));

					// Damage
					player.Damage(hpEach, DamageType.NONE);
				}
			}

			yield return Timing.WaitForSeconds(4);

			// Kill SCP

			Server.Map.Shake();
			var scpCount = RoundSCP.Count;
			for (int i = scpCount > 1 ? 1 : 0; i < scpCount; i++)
			{
				var scp = roundSCP[i];

				if (scp.TeamRole.Team == Smod2.API.Team.SCP)
				{
					scp.ChangeRole(Role.SPECTATOR);
					PersonalBroadcast(scp, 10, $"You will respawn soon, please wait...");
				}
			}

			yield return Timing.WaitForSeconds(15);

			Server.Map.AnnounceCustomMessage("DANGER . SCP 9 3 9 CONTAINMENT BREACH . . . CAUTION . THEY BYTE");


			// Spawn SCP
			var nutCount = RoundSCP.Count;
			if (nutCount > 1)
			{
				var nut = RoundSCP[0];
				nut.ChangeRole(Role.SCP_079);

				PersonalBroadcast(nut, 10, "Help SCP 939 catch Class D, they can't see players that are sneaking around.\n" +
					"Press tab to open the map. You can close and lock doors on players.");
			}
			else if (nutCount == 1)
			{
				var scp = RoundSCP[0];
				scp.ChangeRole(UnityEngine.Random.Range(0f, 1f) > 0.5f ? Role.SCP_939_53 : Role.SCP_939_89);
				scp.Teleport(Server.Map.GetRandomSpawnPoint(Role.FACILITY_GUARD));
			}

			for (int i = 1; i < nutCount; i++)
			{
				var scp = RoundSCP[0];
				scp.ChangeRole(UnityEngine.Random.Range(0f, 1f) > 0.5f ? Role.SCP_939_53 : Role.SCP_939_89);
				scp.Teleport(Server.Map.GetRandomSpawnPoint(Role.FACILITY_GUARD));
			}

			isIntermission = false;

			yield return Timing.WaitForSeconds((lastPhaseNukeTimerMinutes * 60) - 120);

			map.AnnounceCustomMessage("YOU . WILL . NOT . ESCAPE");

			yield return Timing.WaitForSeconds(6);

			Server.Map.StartWarhead();
		}

		/// <summary>
		/// Makes sure a larry teleports every # seconds and creates a new portal after
		/// </summary>
		private IEnumerator<float> LarryTimer(Player player)
		{
			yield return Timing.WaitForSeconds(1);

			PersonalBroadcast(player, 10, $"You will automatically teleport every {secondsUntil106Teleport} seconds.");
			var playerScript = (player.GetGameObject() as GameObject).GetComponent<Scp106PlayerScript>();
			int timer = secondsUntil106Teleport - 4;

			yield return Timing.WaitForSeconds(1);

			allowPortal = true;
			playerScript.CallCmdMakePortal();
			yield return Timing.WaitForSeconds(0.5f);
			allowPortal = false;

			while (currentGameState == ZoneType.HCZ)
			{
				yield return Timing.WaitForSeconds(1);
				timer -= 1;

				if (timer <= 10)
				{
					PersonalBroadcast(player, 1, $"Teleport in {timer}");
				}
				if (timer <= 0)
				{
					timer = secondsUntil106Teleport;

					autoTeleport = true;
					playerScript.CallCmdUsePortal();
				}
			}
		}

		public void OnCheckEscape(PlayerCheckEscapeEvent ev)
		{
			if (endingGame) return;

			ev.ChangeRole = Role.SPECTATOR;
			EscapedPlayers.Add(ev.Player);
			PersonalBroadcast(ev.Player, 10, "Congratulations! You have escaped the facility!");
			CheckEndPhase3(false, false);
		}

		#endregion

		#region Custom Broadcasts

		private void PersonalClearBroadcasts(Player player)
		{
			var connection = (player.GetGameObject() as GameObject).GetComponent<NicknameSync>().connectionToClient;

			if (connection == null)
			{
				return;
			}

			cachedBroadcast.CallTargetClearElements(connection);
		}

		private void PersonalBroadcast(Player player, uint duration, string message)
		{
			var connection = (player.GetGameObject() as GameObject).GetComponent<NicknameSync>().connectionToClient;

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

		#endregion
	}
}