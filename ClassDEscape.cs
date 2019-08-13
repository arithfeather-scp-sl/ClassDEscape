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
/// Force open entrance checkpoint
/// 
/// -game play-
/// -Phase 1- (LCZ)
/// Intro sequence
/// All locked doors are forced Open, players can not close them
/// Decontamination 5 minutes. (editable)
/// No respawns yet. Dead players cry. Players joining move to phase 2. 
/// handle disconnects, check elevator list, check round end.
/// scp dies first on decontamination, players have ~40 seconds to escape then.
/// round ends if SCP = 0 or players = 0. scp win if no survivals.
/// 
/// -Phase 2-
/// decontamination starts if it hasn't started yet. (waits for countdown if in the process)
/// players spawn in elevators with original items/hp and a flashylight. Lift goes up. Broadcast
/// SCP respawn as doggo and comp.
/// handle disconnects, check round end.
/// flicker lights every 8 seconds - can only flicker heavy lights
/// Start open door timer. Players cannot use elevator until timer is up, broadcast message.
/// Generators timer reduced. generators reduce open door timer.
/// 
/// 
/// -todo-
/// Idea** intercom - respawn dead survivors?
/// Idea** Make it the SCP's job to turn off the nuke. Set off nuke on generators down?
/// make sure SCP079 can't close certain doors.
/// remove ammo packs on death
/// Make SCP173 release time changable
/// change to HCZ only, change generators to use cassie announcements?
/// make is so cards respawn in light when a player with a card leaves
/// Fix item broken spawns in lockers.
/// Less final ending. Give scores "You killed this many""This many escaped"
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
		IEventHandlerPlayerJoin, IEventHandlerElevatorUse, IEventHandlerDoorAccess,
		IEventHandlerCheckEscape, IEventHandlerRoundStart, IEventHandlerCallCommand, IEventHandler079AddExp,
		IEventHandlerGeneratorFinish, IEventHandlerPlayerDie, IEventHandlerDisconnect,
		IEventHandlerTeamRespawn, IEventHandlerPlayerHurt, IEventHandlerLCZDecontaminate
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
		[ConfigOption] private readonly bool showGameStartMessage = true;
		[ConfigOption] private readonly bool useDefaultConfig = true;
		[ConfigOption] private readonly float expMultiplier = 50f;
		[ConfigOption] private readonly float peanutStartHP = 0.8f;
		[ConfigOption] private readonly bool skipIntro = false;
		[ConfigOption] private readonly int endScreenSpeed = 10;
		[ConfigOption] private readonly float escapeTimerMinutes = 30f;
		[ConfigOption] private readonly float timeReductionPerGeneratorMinutes = 5f;

		private readonly FieldInfo cachedPlayerConnFieldInfo = typeof(SmodPlayer).GetField("conn", BindingFlags.NonPublic | BindingFlags.Instance);

		//++ Round Data

		private ZoneType currentGameState;
		private bool roundStarted;

		private Broadcast cachedBroadcast;
		private List<Door> cachedDoors;
		private List<Door> CachedDoors => cachedDoors ?? (cachedDoors = new List<Door>());
		private GameObject cachedHost;
		private Room[] cachedRooms;
		private Vector cached173DoorPos;

		//+ Phase 1

		private List<Door> cachedPrisonDoors;
		private List<Door> CachedPrisonDoors => cachedPrisonDoors ?? (cachedPrisonDoors = new List<Door>());
		private List<ElevatorPlayer> playersReachedElevator;
		private List<ElevatorPlayer> PlayersReachedElevator => playersReachedElevator ?? (playersReachedElevator = new List<ElevatorPlayer>());

		private class ElevatorPlayer
		{
			public Player player;
			public Elevator Elevator;
			public int HP;
			public List<ItemType> Inventory = new List<ItemType>();

			public ElevatorPlayer(Player player, Elevator elevator)
			{
				this.player = player;
				Elevator = elevator;

				HP = player.GetHealth();
				foreach (var item in player.GetInventory())
				{
					if (item.ItemType != ItemType.ZONE_MANAGER_KEYCARD)
					{
						Inventory.Add(item.ItemType);
					}
				}
			}
		}

		//+ Phase 2

		private int flickerTimer;
		private int escapeTimer;
		private int currentAnnouncement;

		private List<Room> cachedHCZRooms;
		private List<Room> CachedHCZRooms => cachedHCZRooms ?? (cachedHCZRooms = new List<Room>());

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

		// Plugin Methods

		public override void OnDisable() => Info(Details.name + " was disabled");
		public override void OnEnable() => Info($"{Details.name} has loaded, type 'fe' for details.");
		public override void Register() => AddEventHandlers(this);

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
				case "173_door_starting_cooldown":
					//todo 
					// Can't be edited because built into voice. I need to make the audio longer by 1 second = 1 period. (Fix this later)
					ev.Value = (int)TotalTimeUntil173;
					break;

				case "disable_decontamination": //todo 
					if (useDefaultConfig) ev.Value = false;
					break;

				case "decontamination_time":

					if (useDefaultConfig)
					{
						// Default 5 minutes
						ev.Value = 4.44f + ((TotalTimeUntil173 + TimeUntilDecontam) / 60f);
					}
					else
					{
						ev.Value = (float)ev.Value + ((TotalTimeUntil173 + TimeUntilDecontam) / 60f);
					}
					break;

				case "team_respawn_queue":
					if (useDefaultConfig) ev.Value = "404044440444440444444044444440";
					break;
				case "generator_duration":
					if (useDefaultConfig) ev.Value = 5f;
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
			escapeTimer = (int)(escapeTimerMinutes * 60);
			PlayersReachedElevator.Clear();
			CachedDoors.Clear();
			CachedPrisonDoors.Clear();
			currentGameState = ZoneType.LCZ;
			RandomItemSpawner.RandomItemSpawner.Instance.UseDefaultEvents = false;

			cachedRooms = Server.Map.Get079InteractionRooms(Scp079InteractionType.CAMERA);
			CachedHCZRooms.Clear();

			// Cache HCZ rooms for flicker lights
			foreach (var room in cachedRooms)
			{
				if (room.ZoneType == ZoneType.HCZ && room.RoomType != RoomType.UNDEFINED)
				{
					CachedHCZRooms.Add(room);
				}
			}

			// Lock Class D Doors and cache for later opening
			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;
				if (door.doorType == 0 && door.name.StartsWith("PrisonDoor"))
				{
					door.Networklocked = true;
					CachedPrisonDoors.Add(door);
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
			var numPlayers = Server.NumPlayers;

			const float ratio = 0.834f; // 5/6
			var averageSpawnCount = 5 + (int)(numPlayers / ratio); // 30 = 30 || 10 = 13
			var halfAv = (int)(averageSpawnCount / 3); // 30 = 10 || av. 10 = 4

			// LCZ (27 spawns) 30 = 33 || 10 = 16
			itemSpawner.SpawnItems(3, ZoneType.LCZ, ItemType.ZONE_MANAGER_KEYCARD);
			itemSpawner.SpawnItems(averageSpawnCount, ZoneType.LCZ, ItemType.RADIO);

			//todo Group spawn later? Remove all LCZ spawn points.
			// HCZ (39) 30 = 46 || 10 = 26
			itemSpawner.ResetRoomIndexer();
			itemSpawner.SpawnItems(1, ZoneType.HCZ, ItemType.CHAOS_INSURGENCY_DEVICE);
			itemSpawner.SpawnItems(2, ZoneType.HCZ, ItemType.WEAPON_MANAGER_TABLET);
			itemSpawner.SpawnItems(halfAv, ZoneType.HCZ, ItemType.RADIO);
			itemSpawner.SpawnItems(averageSpawnCount, ZoneType.HCZ, ItemType.MEDKIT);

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


			if (skipIntro)
			{
				Timing.RunCoroutine(QuickRoundStart());
			}
			else
			{
				Timing.RunCoroutine(RoundStart());
			}
			//todo testing below - instant start phase 2

			//var p = Server.GetPlayers(Role.CLASSD)[0];
			//OnPlayerJoin(new PlayerJoinEvent(p));
			//p.ChangeRole(Role.SPECTATOR, false, false, false);
			//CheckEndPhase1(0);
		}

		#endregion

		#region Shared events

		public void OnPlayerJoin(PlayerJoinEvent ev)
		{
			if (showGameStartMessage)
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
						var eles = Server.Map.GetElevators();
						for (int i = 0; i < eles.Count; i++)
						{
							var el = eles[i];
							if (el.ElevatorType == ElevatorType.LiftA || el.ElevatorType == ElevatorType.LiftB)
							{
								PlayersReachedElevator.Add(new ElevatorPlayer(ev.Player, el));
								break;
							}
						}

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
			if (ev.Player.GetRankName().ToUpper() == "OWNER")
				Server.Map.AnnounceCustomMessage(ev.Command);


			//todo display game help
			//switch (ev.Command.ToUpper())
			//{
			//	case "HELP":
			//		break;
			//}
		}

		public void OnTeamRespawn(TeamRespawnEvent ev) => ev.PlayerList.Clear();

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
						player.ChangeRole(Role.SPECTATOR, true, false, false);
						PersonalBroadcast(player, 5, "You have escaped to heavy! Please wait for the other players");
						CheckEndPhase1(Round.Stats.ClassDAlive);
					}

					break;

				case ZoneType.HCZ:

					var elevatorType = ev.Elevator.ElevatorType;
					if (elevatorType == ElevatorType.GateA || elevatorType == ElevatorType.GateB)
					{
						ev.AllowUse = false;

						if (ev.Player.TeamRole.Team != Smod2.API.Team.SCP)
						{
							PersonalBroadcast(ev.Player, 3, $"Facility is still on lockdown for {escapeTimer} seconds.");
						}
					}

					break;

				case ZoneType.ENTRANCE:
					break;
			}
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
							ev.Damage = PeanutDeconDamage;
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
					break;
			}
		}

		#endregion

		#region Phase 1

		private const float TimeUntilStart = 29.5f;
		private const float TimeUntilIntroduction = 5f;
		private const float TimeUntil173 = 16f; // 21 seconds after freedom
		private const float TimeUntilDecontam = 8f;
		private const float TotalTimeUntil173 = TimeUntilStart + TimeUntilIntroduction + TimeUntil173;

		public void OnDecontaminate()
		{
			if (currentGameState == ZoneType.LCZ) Timing.RunCoroutine(KillSCPDecontamination());
		}

		private const int KillPeanutInSeconds = 10;
		private const int PeanutHP = 3200;
		private const int PeanutDeconDamage = (int)(((PeanutHP / 2) / KillPeanutInSeconds) / 4f);

		private IEnumerator<float> KillSCPDecontamination()
		{
			Timing.RunCoroutine(DecontaminationPlayerDamage());

			yield return Timing.WaitForSeconds(KillPeanutInSeconds + 2);

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

		private void CheckEndPhase1(int classDAlive)
		{
			//todo testing
			//if (Round.Stats.SCPAlive == 0)
			//{
			//ClassDWin();
			//}
			//else 
			if (classDAlive == 0)
			{
				if (PlayersReachedElevator.Count == 0)
				{
					SCPWin();
				}
				else
				{
					Timing.RunCoroutine(StartPhase2());
				}
			}
		}

		private IEnumerator<float> RoundStart()
		{
			Server.Map.AnnounceCustomMessage("UNAUTHORIZED ACCESS . . WARNING . SCP 0 7 . . . . PASSWORD ACCEPTED . ACCESS GRANTED . IN INSTALLING SOFTWARE . . . . . . COMPLETED . CRITICAL MALFUNCTION CORRUPTED DATA MEMORY UNSTABLE FACILITY IS NOW ON LOCKDOWN DOWN CORE OR ME ME ME");

			yield return Timing.WaitForSeconds(TimeUntilStart - 1.5f);

			FlickerLCZLights();

			yield return Timing.WaitForSeconds(1.5f);

			StartGameClassD();

			yield return Timing.WaitForSeconds(TimeUntilIntroduction);

			Server.Map.AnnounceCustomMessage(". . . . . I AM S C P 0 7 9 . . . YOU WILL ALL BE EXECUTED . . . . . . . . . SCP 1 7 3 CONTAINMENT BREACH");

			yield return Timing.WaitForSeconds(TimeUntil173);

			StartGameSCP();
		}

		private IEnumerator<float> QuickRoundStart()
		{
			StartGameClassD();

			yield return Timing.WaitForSeconds(TimeUntilIntroduction + TimeUntil173);

			Server.Map.AnnounceCustomMessage("SCP 1 7 3 CONTAINMENT BREACH");

			StartGameSCP();

			yield return Timing.WaitForSeconds(TimeUntilDecontam);

			var decon = cachedHost.GetComponent<DecontaminationLCZ>() as DecontaminationLCZ;
			decon.time = decon.announcements[decon.GetCurAnnouncement()].startTime * 60;
		}

		/// <summary>
		/// Called when Class D can leave cells
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void StartGameClassD()
		{
			// Unlock/open class d. open locked doors
			var doorCount = CachedPrisonDoors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedPrisonDoors[i];
				door.Networklocked = false;
				door.NetworkisOpen = true;
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

			// Open Locked doors and peanut door
			var doorCount = CachedDoors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedDoors[i];

				if (door.doorType == 2)
				{
					door.NetworkisOpen = true;
				}
				else if (door.doorType == 0 && door.transform.parent.name == "MeshDoor173")
				{
					door.NetworkisOpen = true;
					cached173DoorPos = Tools.Vec3ToVec(door.localPos);
				}
			}
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

		#endregion

		#region Phase 2

		public void On079AddExp(Player079AddExpEvent ev) => ev.ExpToAdd *= expMultiplier;

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

		private IEnumerator<float> StartPhase2()
		{
			// Change SCP to doggo/Comp
			var nuts = Server.GetPlayers(Smod2.API.Team.SCP);
			var nutCount = nuts.Count;

			for (int i = 0; i < nutCount; i++)
			{
				nuts[i].Teleport(cached173DoorPos);
			}

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
					typeof(DecontaminationLCZ).GetField("curAnm", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(decon, 5);
					decon.time = 704.4f;
				}
			}

			yield return Timing.WaitForSeconds(2);

			// Run elevators
			var elevators = Server.Map.GetElevators();
			foreach (var elevator in elevators)
			{
				if (elevator.ElevatorType == ElevatorType.LiftA || elevator.ElevatorType == ElevatorType.LiftB)
				{
					elevator.MovingSpeed = 10;
					elevator.Locked = false;
					elevator.Use();
					elevator.Locked = true;
				}
			}

			// Doors elevator close
			yield return Timing.WaitForSeconds(1);

			// Spawn survivors
			var epCount = PlayersReachedElevator.Count;
			for (int i = 0; i < epCount; i++)
			{
				var ep = PlayersReachedElevator[i];
				var player = ep.player;

				player.ChangeRole(Role.CLASSD, true, false, false);

				// Position inside the elevator they clicked.
				player.Teleport(Tools.Vec3ToVec((ep.Elevator.GetComponent() as Lift).transform.position + new Vector3(0, 1.35f, 0)));

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

				// Return Damage
				var damage = 100 - ep.HP;
				if (damage > 0) player.Damage(damage, DamageType.NONE);

				// Broadcast
				PersonalBroadcast(player, 10, "Activate the generators to make the exit open faster.");
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
				else if (door.doorType == 3)
				{
					door.OpenDecontamination();
				}
			}

			yield return Timing.WaitForSeconds(14);

			Server.Map.Shake();
			FlickerHCZLights();

			yield return Timing.WaitForSeconds(1);


			//todo testing
			// Change SCP to doggo/Comp
			nuts = Server.GetPlayers(Smod2.API.Team.SCP);
			nutCount = nuts.Count;

			// Spawn SCP079 instead of doggo if 2 or more SCP present.
			nuts[0].ChangeRole(nutCount > 1 ? Role.SCP_079 : UnityEngine.Random.Range(0f, 1f) > 0.5f ? Role.SCP_939_53 : Role.SCP_939_89);

			for (int i = 1; i < nutCount; i++)
			{
				nuts[i].ChangeRole(UnityEngine.Random.Range(0f, 1f) > 0.5f ? Role.SCP_939_53 : Role.SCP_939_89);
			}

			Server.Map.AnnounceCustomMessage("WARNING . POWER FAILURE . . SCP 9 3 9 CONTAINMENT BREACH . . . CAUTION . THEY BYTE");

			yield return Timing.WaitForSeconds(7);

			FlickerHCZLights();

			yield return Timing.WaitForSeconds(3);

			var announceLength = announcements.Length;
			UpdateCurrentAnnouncement();

			flickerTimer = 5;
			Timing.RunCoroutine(Counters());

			// Start countdown / Update every second
			while (currentGameState == ZoneType.HCZ)
			{
				// Check end 
				if (currentAnnouncement < announceLength)
				{
					var currAnnounce = announcements[currentAnnouncement];

					if (escapeTimer == currAnnounce.StartTime)
					{
						currentAnnouncement++;

						Server.Map.AnnounceCustomMessage($"POWER REACTIVATION IN T MINUS {currAnnounce.Text}");
					}
				}
				else if (escapeTimer < 6)
				{
					yield return Timing.WaitForSeconds(GeneratorSpeechTime);

					var weirdWaitTimeThing = flickerTimer - GeneratorSpeechTime;
					if (weirdWaitTimeThing > 0)
					{
						yield return Timing.WaitForSeconds(weirdWaitTimeThing);
					}

					Server.Map.AnnounceCustomMessage($"POWER REACTIVATION");

					currentGameState = ZoneType.ENTRANCE;

					yield return Timing.WaitForSeconds(1);

					OpenFacility();

					yield break;
				}
				else if (escapeTimer <= 13)
				{
					var staticTimer = escapeTimer;
					escapeTimer -= 3;
					var flickerTimerRequires = escapeTimer % 8;
					var pauseDuration = flickerTimer - flickerTimerRequires;

					if (pauseDuration < 0)
					{
						pauseDuration += 8;
					}
					else if (pauseDuration == 8)
					{
						pauseDuration = 0;
					}

					if (pauseDuration > 0)
					{
						yield return Timing.WaitForSeconds(pauseDuration);
					}

					var dumbCountDownBuilder = new StringBuilder(80);
					dumbCountDownBuilder.Clear();
					dumbCountDownBuilder.Append("POWER REACTIVATION IN ");

					for (int i = staticTimer - 3; i >= 1; i--)
					{
						dumbCountDownBuilder.Append(i);
						dumbCountDownBuilder.Append(" . ");
					}

					Server.Map.AnnounceCustomMessage(dumbCountDownBuilder.ToString());

					yield return Timing.WaitForSeconds(staticTimer - 2);

					currentGameState = ZoneType.ENTRANCE;

					yield return Timing.WaitForSeconds(1);

					OpenFacility();

					yield break;
				}

				yield return Timing.WaitForSeconds(1);
			}
		}

		private IEnumerator<float> Counters()
		{
			while (currentGameState == ZoneType.HCZ)
			{
				escapeTimer--;
				flickerTimer--;

				if (flickerTimer == 0)
				{
					flickerTimer = 8;

					FlickerHCZLights();
				}

				yield return Timing.WaitForSeconds(1);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void FlickerHCZLights()
		{
			foreach (var room in CachedHCZRooms)
			{
				room.FlickerLights();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void OpenFacility()
		{
			// Unlock entrance doors?
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void UpdateCurrentAnnouncement()
		{
			var announceLength = announcements.Length;

			for (int i = 0; i < announceLength; i++)
			{
				var ananas = announcements[i];

				if (escapeTimer >= ananas.StartTime)
				{
					currentAnnouncement = i;
					return;
				}
			}

			currentAnnouncement = announceLength;
		}

		public void OnGeneratorFinish(GeneratorFinishEvent ev)
		{
			escapeTimer -= (int)(timeReductionPerGeneratorMinutes * 60);
			UpdateCurrentAnnouncement();
		}

		#endregion

		#region Phase 3

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

		#endregion
	}
}