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
using Smod2.Lang;
using ArithFeather.CustomAPI;

/// <summary>
/// Test Decontamination checkpoint staying close and not decontamination locked
/// 
/// -todo-
/// remove ammo packs on death
/// 
/// -low priority-
/// Make 914 useful.
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
		langFile = nameof(ClassDEscape),
		SmodMajor = 3,
		SmodMinor = 4,
		SmodRevision = 0
		)]
	public class ClassDEscape : Plugin, IEventHandlerWaitingForPlayers, IEventHandlerSetConfig, IEventHandlerSetRole,
		IEventHandlerPlayerJoin, IEventHandlerElevatorUse, IEventHandlerDoorAccess, IEventHandlerRoundRestart,
		IEventHandlerCheckEscape, IEventHandlerRoundStart, IEventHandlerCallCommand, IEventHandler079AddExp, IEventHandlerPlayerDie,
		IEventHandlerSCP914ChangeKnob, IEventHandlerPlayerHurt, IEventHandlerLCZDecontaminate, IEventHandlerWarheadStopCountdown,
		IEventHandlerWarheadKeycardAccess, IEventHandlerWarheadDetonate, IEventHandler079Door
	{
		public const string ModVersion = "1.1";

		#region Text values

		private const string ServerHighLightColor = "#38a8b5";

		private const string ServerInfoColor = "#23eb44";
		private const int ServerInfoSize = 50;

		private const string ClassInfoColor = "#23eb44";
		private const int ClassInfoSize = 50;

		private const string WinColor = "#b81111";
		private const int WinTextSize = 70;

		private static readonly string JoinGameMessage1 =
			$"<size={ServerInfoSize}><color={ServerInfoColor}>Welcome to <color={ServerHighLightColor}>Class D Escape v{ModVersion}! (beta testing in progress)</color> Press ` to open the console and enter '<color={ServerHighLightColor}>.help</color>' for mod information!</color></size>";
		private static readonly string JoinGameMessage2 =
			$"<size={ServerInfoSize}><color={ServerInfoColor}>If you like the plugin, join the discord for updates!\n <color={ServerHighLightColor}>https://discord.gg/DunUU82</color></color></size>";

		private readonly string helpMessage = "Class D Escape Mode v{0}\nhttps://discord.gg/DunUU82\nClass D Objective is to escape the facility without getting killed. Difficulty is hard.\nSCP need to kill all the Class D before they escape\nCard keys and other loot spawn randomly around the facility. Look near tables, chairs, shelves, etc.\nSCP914 is disabled. Nuke is disabled. Players respawn each phase.\nPhase 1: Find a keycard and escape through checkpoint to elevator\nPhase 2: watch out for the randomly teleporting Larry and Shyguy in the dark. Use your flashlight carefully.\nPhase 2: Activate generators to reduce the power reactivation countdown. This will begin phase 3.\nPhase 3: Find the black keycard and escape the facility. Watch out for the Dog and Computer SCP.";

		#endregion

		#region Configs

		[ConfigOption] private readonly bool disablePlugin = false;
		[ConfigOption] private readonly bool showPlayerJoinMessage = true;
		[ConfigOption] private readonly float expMultiplier = 25f;
		[ConfigOption] private readonly float peanutHealthPercent = 1f;
		[ConfigOption] private readonly bool skipIntro = false;
		[ConfigOption] private readonly float lastPhaseNukeTimerMinutes = 5;
		[ConfigOption] private readonly int lczKeySpawns = 3;
		[ConfigOption] private readonly int hczKeyTabletSpawns = 4;
		[ConfigOption] private readonly int entranceKeySpawns = 2;

		// Called once on register
		private bool useDefaultConfig; // manually get on register

		private float timeUntil173;
		private float deconTime;

		#endregion

		//++ Round Data

		private ZoneType currentGameState = ZoneType.UNDEFINED;
		private bool roundStarted = false;
		private bool isIntermission;
		private int endScreenSpeedSeconds = 15;

		private GameObject cachedHost;

		private List<Player> roundSCP;
		private List<Player> RoundSCP => roundSCP ?? (roundSCP = new List<Player>(3));
		private List<Player> roundPlayers;
		private List<Player> RoundPlayers => roundPlayers ?? (roundPlayers = new List<Player>(17));

		private CoroutineHandle gameFlowCoroutine;

		private bool IsGameEnding => CustomAPI.CustomAPI.IsGameEnding;

		//+ Phase 1

		private List<ElevatorPlayer> playersReachedElevator;
		private List<ElevatorPlayer> PlayersReachedElevator => playersReachedElevator ?? (playersReachedElevator = new List<ElevatorPlayer>(14));
		private List<Elevator> lczElevators;
		private List<Elevator> LczElevators => lczElevators ?? (lczElevators = new List<Elevator>(4));

		private List<Door> cachedPrisonDoors;
		private List<Door> CachedPrisonDoors => cachedPrisonDoors ?? (cachedPrisonDoors = new List<Door>(30));
		private List<Door> cachedLczDoors;
		private List<Door> CachedLczDoors => cachedLczDoors ?? (cachedLczDoors = new List<Door>(70));
		private Door cached173Door;

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
		private readonly FieldInfo roundEndedInfo = typeof(RoundSummary).GetField("roundEnded", BindingFlags.NonPublic | BindingFlags.Instance);

		//+ Phase 2

		private PowerOuttage powerOuttage;
		private LarryController LarryController;
		private bool beginPhase2;
		private Door cachedEntranceChkPnt;

		//+ Phase 3

		private List<Elevator> hczElevators;
		private List<Elevator> HczElevators => hczElevators ?? (hczElevators = new List<Elevator>(2));

		private List<Player> escapedPlayers;
		private List<Player> EscapedPlayers => escapedPlayers ?? (escapedPlayers = new List<Player>(17));

		private bool beginPhase3;

		// Plugin Methods

		public override void OnDisable() => Info(Details.name + " was disabled");
		public override void OnEnable() => Info($"{Details.name} has loaded, type 'fe' for details.");
		public override void Register()
		{
			CustomAPI.CustomAPI.Initialize(this);
			CustomAPI.CustomAPI.OnPlayerDisconnect += OnPlayerDisconnect;

			powerOuttage = new PowerOuttage(this);
			powerOuttage.OnPowerReactivation += OnPowerReactivation;

			LarryController = new LarryController(this);

			AddEventHandlers(this);

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
					if (useDefaultConfig) ev.Value = "04404444444444044444";
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
			isIntermission = false;

			var configFile = ConfigManager.Config;
			endScreenSpeedSeconds = useDefaultConfig ? 15 : configFile.GetIntValue("auto_round_restart_time", 15);

			RoundSCP.Clear();
			RoundPlayers.Clear();

			RandomItemSpawner.RandomItemSpawner.Instance.UseDefaultEvents = false;

			SetupDoors();

			// Elevators
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
					case ElevatorType.WarheadRoom:
					case ElevatorType.SCP049Chamber:
						HczElevators.Add(elevator);
						break;
				}
			}

			// Phase 1
			PlayersReachedElevator.Clear();
			Scp914.singleton.working = true; // Makes it so it can't be used

			// Phase 2
			beginPhase2 = false;

			// Phase 3
			beginPhase3 = false;
			EscapedPlayers.Clear();
		}

		/// <summary>
		/// Also locks Class D and SCP 173 Doors
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void SetupDoors()
		{
			CachedLczDoors.Clear();
			CachedPrisonDoors.Clear();

			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			bool found173Door = false;
			bool foundchkpnt = false;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;

				door.dontOpenOnWarhead = true;

				Scp079Interactable component = door.GetComponent<Scp079Interactable>();
				if (component != null)
				{
					Scp079Interactable.ZoneAndRoom zoneAndRoom = component.currentZonesAndRooms[0];
					switch (zoneAndRoom.currentZone)
					{
						case "HeavyRooms":

							// Open locked hcz doors
							if (door.doorType == 2)
							{
								door.NetworkisOpen = true;
							}
							else if (!foundchkpnt && door.doorType == 3)
							{
								foundchkpnt = true;
								cachedEntranceChkPnt = door;
							}

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
							// Open Locked doors
							else if (door.doorType == 2)
							{
								door.NetworkisOpen = true;
							}

							break;
						case "EntranceRooms":

							if (door.doorType == 2)
							{
								door.permissionLevel = "CONT_LVL_3";
							}

							break;
					}
				}
			}
		}

		public void OnRoundStart(RoundStartEvent ev)
		{
			if (IsGameEnding) return;

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

			// Spawn custom items
			var itemSpawner = RandomItemSpawner.RandomItemSpawner.Instance.ItemSpawning;
			var numPlayers = Server.NumPlayers - 1;

			const float ratio = 0.75f; // 15/20 = 3/4
			var averageSpawnCount = 5 + (int)(numPlayers * ratio); // 20 = 20 || 10 = 13
			var halfAv = (int)(averageSpawnCount / 2); // 20 = 10 || av. 10 = 6

			// LCZ (32 spawns) 20 = 23 || 10 = 16
			itemSpawner.SpawnItems(lczKeySpawns, ZoneType.LCZ, ItemType.ZONE_MANAGER_KEYCARD);
			itemSpawner.SpawnItems((int)(averageSpawnCount * 1.5f), ZoneType.LCZ, ItemType.RADIO);

			// HCZ (31) 20 = 16 | 10 = 12
			itemSpawner.ResetRoomIndexer();
			itemSpawner.SpawnItems(hczKeyTabletSpawns, ZoneType.HCZ, ItemType.CHAOS_INSURGENCY_DEVICE);
			itemSpawner.SpawnItems(hczKeyTabletSpawns, ZoneType.HCZ, ItemType.WEAPON_MANAGER_TABLET);
			itemSpawner.SpawnItems(halfAv, ZoneType.HCZ, ItemType.RADIO);
			itemSpawner.SpawnItems(halfAv, ZoneType.HCZ, ItemType.MEDKIT);

			// Entrance (38)
			itemSpawner.ResetRoomIndexer();
			itemSpawner.SpawnItems(entranceKeySpawns, ZoneType.ENTRANCE, ItemType.O5_LEVEL_KEYCARD);
			itemSpawner.SpawnItems((int)(halfAv), ZoneType.ENTRANCE, ItemType.MEDKIT);

			gameFlowCoroutine = Timing.RunCoroutine(_GameFlow());
		}

		#endregion

		#region Shared events

		private IEnumerator<float> _GameFlow()
		{
			const int ClassDHP = 100;
			var map = Server.Map;

			#region Phase 1

			currentGameState = ZoneType.LCZ;

			if (!skipIntro)
			{
				map.AnnounceCustomMessage(startCassieMessages[UnityEngine.Random.Range(0, startCassieMessages.Length)]);

				yield return Timing.WaitForSeconds(3);
				yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);
			}

			StartGameClassD();

			yield return Timing.WaitForSeconds(timeUntil173);

			StartGameSCP();

			map.AnnounceCustomMessage("DANGER . SCP 1 7 3 CONTAINMENT BREACH");

			yield return Timing.WaitForSeconds(3);
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			StartDecontamination();

			yield return Timing.WaitForSeconds(TimeUntilIntroduction);

			map.AnnounceCustomMessage(lczCassieMessages[UnityEngine.Random.Range(0, lczCassieMessages.Length)]);

			yield return Timing.WaitForSeconds(0.5f);

			#endregion

			yield return Timing.WaitUntilTrue(() => beginPhase2);
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			#region Phase 2

			isIntermission = true;
			currentGameState = ZoneType.HCZ;

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
					decon.time = 703.4f;
				}
			}

			// Wait for decontamination event
			yield return Timing.WaitForSeconds(1.5f);

			// Send nuts back home so they can't sneak in the elevators
			var scpCount = RoundSCP.Count;
			var nutSpawnPos = map.GetSpawnPoints(Role.SCP_173)[0];
			for (int i = 0; i < scpCount; i++)
			{
				RoundSCP[i].Teleport(nutSpawnPos);
			}

			// Run elevators
			foreach (var elevator in LczElevators)
			{
				elevator.MovingSpeed = 10;
				elevator.Locked = false;
				elevator.Use();
			}

			// Doors elevator close
			yield return Timing.WaitForSeconds(1);

			// Re-lock elevators
			foreach (var elevator in LczElevators)
			{
				elevator.Locked = true;
			}

			// Spawn survivors
			var playerCount = RoundPlayers.Count;
			var epCount = PlayersReachedElevator.Count;
			int hpEach = (int)((ClassDHP * epCount) / playerCount);
			var deadPlayers = playerCount - epCount;

			if (deadPlayers > 1)
			{
				CustomAPI.CustomAPI.Broadcast(8, $"{deadPlayers} dead players have been respawned. All players now have {hpEach} HP.");
			}
			else if (deadPlayers == 1)
			{
				CustomAPI.CustomAPI.Broadcast(8, $"1 dead player has been respawned. All players now have {hpEach} HP.");
			}

			hpEach = Mathf.Clamp(ClassDHP - hpEach, 1, ClassDHP);

			for (int i = 0; i < playerCount; i++)
			{
				var player = RoundPlayers[i];

				player.ChangeRole(Role.CLASSD, true, false);

				// Equal HP
				if (hpEach > 0) player.Damage(hpEach);

				player.GiveItem(ItemType.FLASHLIGHT);

				player.Broadcast(10, "Activate the generators to make the exit open faster.\n Hint: Use your flashlight.");

				bool playerReachedElevator = false;
				for (int j = 0; j < epCount; j++)
				{
					var ep = PlayersReachedElevator[j];

					if (ep.player.PlayerId == player.PlayerId)
					{
						// Position inside the elevator they clicked.
						player.Teleport(Tools.Vec3ToVec((ep.Elevator.GetComponent() as Lift).transform.position + new Vector3(0, 1.35f, 0)));

						// Return Inventory
						var inv = ep.Inventory;
						var invCount = ep.Inventory.Count;
						if (invCount > 0)
						{
							for (int h = 0; h < invCount; h++)
							{
								player.GiveItem(inv[h]);
							}
						}

						playerReachedElevator = true;
						break;
					}
				}

				if (!playerReachedElevator)
				{
					// Position inside random elevator
					var randomEv = LczElevators[UnityEngine.Random.Range(0, LczElevators.Count)];
					player.Teleport(Tools.Vec3ToVec((randomEv.GetComponent() as Lift).transform.position + new Vector3(0, 1.35f, 0)));
				}
			}

			yield return Timing.WaitForSeconds(14);

			// Start lights out
			map.Shake();
			CustomAPI.CustomAPI.HczLightsOff();

			yield return Timing.WaitForSeconds(1);

			// Respawn SCP and broadcast
			var countDownMessage = new StringBuilder(80);
			countDownMessage.Append("DANGER . POWER FAILURE . . SCP 1 0 6");
			scpCount = RoundSCP.Count;
			var spawnShyguy = string.Empty;

			for (int i = 0; i < scpCount; i++)
			{
				if (i == 0 || i > 1)
				{
					RoundSCP[i].ChangeRole(Role.SCP_106);
				}
				else
				{
					spawnShyguy = " and Shyguy";
					RoundSCP[i].ChangeRole(Role.SCP_096);
					countDownMessage.Append(" . AND SCP 0 9 6");
				}
			}
			countDownMessage.Append(" CONTAINMENT BREACH");

			map.AnnounceCustomMessage(countDownMessage.ToString());

			isIntermission = false;
			CheckEndPhase2(false, false);

			yield return Timing.WaitForSeconds(3);
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			powerOuttage.BeginPowerOuttage();

			#endregion

			yield return Timing.WaitUntilTrue(() => beginPhase3);

			#region Phase 3

			currentGameState = ZoneType.ENTRANCE;
			isIntermission = true;

			map.AnnounceCustomMessage("EMERGENCY BACKUP POWER ENGAGED . ENTRANCE CHECKPOINT IS NOW OPEN");

			// Unlock entrance checkpoint
			cachedEntranceChkPnt.OpenDecontamination();

			// Send elevators up, wait until they are both up
			var ev1 = HczElevators[0];
			var ev2 = HczElevators[1];

			do
			{
				if (ev1.ElevatorStatus == ElevatorStatus.Up)
				{
					ev1.MovingSpeed = 2;
					ev1.Use();
				}
				if (ev2.ElevatorStatus == ElevatorStatus.Up)
				{
					ev2.MovingSpeed = 2;
					ev2.Use();
				}

				yield return Timing.WaitForSeconds(1f);
			}
			while (!(ev1.ElevatorStatus == ElevatorStatus.Down && ev2.ElevatorStatus == ElevatorStatus.Down));

			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			// Cache position and send elevators Down
			List<Vector> spawnPoints = new List<Vector>(2);
			foreach (var hczElevator in HczElevators)
			{
				foreach (Lift.Elevator elevator in (hczElevator.GetComponent() as Lift).elevators)
				{
					if (elevator.door.GetBool("isOpen"))
					{
						spawnPoints.Add(Tools.Vec3ToVec(elevator.target.position));
						break;
					}
				}

				hczElevator.MovingSpeed = 5;
				hczElevator.Use();
			}

			// Kill SCP
			CustomAPI.CustomAPI.Broadcast(3, $"Larry{spawnShyguy} disappeared...");

			scpCount = RoundSCP.Count;
			for (int i = 0; i < scpCount; i++)
			{
				var scp = RoundSCP[i];
				scp.ChangeRole(Role.SPECTATOR);
				scp.Broadcast(10, $"You will respawn soon, please wait...");
			}

			// Elevator doors close
			yield return Timing.WaitForSeconds(1);

			// Pool HP
			playerCount = RoundPlayers.Count;
			hpEach = 0;
			var deadPlayercount = 0;

			for (int i = 0; i < playerCount; i++)
			{
				var player = RoundPlayers[i];

				if (player.GetCurrentRole() == Role.CLASSD)
				{
					hpEach += player.GetHealth();
				}
				else
				{
					deadPlayercount++;
				}
			}

			if (deadPlayercount > 0)
			{
				hpEach = Mathf.Clamp((int)(hpEach / playerCount), 1, 100);

				if (deadPlayercount == 1)
				{
					CustomAPI.CustomAPI.Broadcast(7, $"1 dead player has been respawned with {hpEach} HP.");
				}
				else
				{
					CustomAPI.CustomAPI.Broadcast(7, $"{deadPlayercount} dead players have been respawned with {hpEach} HP.");
				}

				hpEach = ClassDHP - hpEach;

				// Damage players
				for (int i = 0; i < playerCount; i++)
				{
					var player = RoundPlayers[i];

					if (player.GetCurrentRole() == Role.SPECTATOR)
					{
						player.ChangeRole(Role.CLASSD, true, false);

						// Position inside random elevator
						player.Teleport(spawnPoints[UnityEngine.Random.Range(0, 2)]);

						// Damage 
						player.Damage(hpEach, DamageType.NONE);
					}
				}
			}

			yield return Timing.WaitForSeconds(10);

			map.AnnounceCustomMessage("DANGER . SCP 9 3 9 CONTAINMENT BREACH . . . CAUTION . THEY BYTE");

			// Spawn SCP
			RoundSCP.Shuffle();
			scpCount = RoundSCP.Count;
			var spawnComputer = scpCount > 1;
			for (int i = 0; i < scpCount; i++)
			{
				var scp = RoundSCP[i];

				if (i == 0 && spawnComputer)
				{
					scp.ChangeRole(Role.SCP_079);

					scp.Broadcast(10, "Help SCP 939 catch Class D, they can't see players that are sneaking around.\n" +
						"Press tab to open the map. You can close and lock doors on players.");
				}
				else
				{
					scp.ChangeRole(UnityEngine.Random.Range(0f, 1f) > 0.5f ? Role.SCP_939_53 : Role.SCP_939_89);
					scp.Teleport(map.GetRandomSpawnPoint(Role.FACILITY_GUARD));
				}
			}

			isIntermission = false;

			yield return Timing.WaitForSeconds((lastPhaseNukeTimerMinutes * 60) - 120);

			map.AnnounceCustomMessage("YOU . WILL . NOT . ESCAPE");

			yield return Timing.WaitForSeconds(6);

			map.StartWarhead();

			#endregion
		}

		public void OnPlayerJoin(PlayerJoinEvent ev)
		{
			var player = ev.Player;

			if (showPlayerJoinMessage)
			{
				try
				{
					player.Broadcast(8, JoinGameMessage1);
					player.Broadcast(8, JoinGameMessage2);
				}
				catch
				{
					Info("Null ref on joining player. Restarting Server...");
				}
			}

			if (roundStarted && !IsGameEnding)
			{
				RoundPlayers.Add(player);
			}
		}

		private void OnPlayerDisconnect(int playerID)
		{
			if (IsGameEnding || !roundStarted) return;

			//Remove cached disconnected player
			bool skipSecondCheck = false;
			for (int i = RoundSCP.Count - 1; i >= 0; i--)
			{
				var player = RoundSCP[i];

				if (player.PlayerId == playerID)
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

					if (player.PlayerId == playerID)
					{
						RoundPlayers.RemoveAt(i);
						break;
					}
				}
			}

			LarryController.DisconnectPlayer(playerID);

			// Check round end on no SCP/Players left
			if (RoundSCP.Count == 0)
			{
				CustomAPI.CustomAPI.Broadcast(endScreenSpeedSeconds, "SCP disconnected. There are no SCP left, restarting round...");
				EndGame(RoundSummary.LeadingTeam.ChaosInsurgency);
			}
			else if (RoundPlayers.Count == 0)
			{
				CustomAPI.CustomAPI.Broadcast(endScreenSpeedSeconds, "Player disconnected. There are no players left, restarting round...");
				EndGame(RoundSummary.LeadingTeam.Anomalies);
			}
			else
			{
				//Update lists
				switch (currentGameState)
				{
					case ZoneType.LCZ:

						RemoveElevatorPlayer(playerID);
						CheckEndPhase1(false);

						break;

					case ZoneType.HCZ:

						RemoveElevatorPlayer(playerID);
						CheckEndPhase2(false, false);

						break;
					case ZoneType.ENTRANCE:

						//Remove escaped player
						var escapedPlayerCount = EscapedPlayers.Count;
						for (int i = 0; i < escapedPlayerCount; i++)
						{
							if (EscapedPlayers[i].PlayerId == playerID)
							{
								EscapedPlayers.RemoveAt(i);
								break;
							}
						}

						CheckEndPhase3(false, false);

						break;
				}
			}
		}

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
			//Server.Map.AnnounceCustomMessage(ev.Command);

			Info($"Players: {RoundPlayers.Count} | SCP: {RoundSCP.Count}");

			switch (ev.Command.ToUpper())
			{
				case "HELP":
					ev.ReturnMessage = string.Format(helpMessage, ModVersion);
					break;
			}
		}

		public void OnElevatorUse(PlayerElevatorUseEvent ev)
		{
			if (IsGameEnding) return;

			if (currentGameState == ZoneType.LCZ)
			{
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
					player.Broadcast(5, "You have escaped to heavy containment! Please wait for the other players...");

					CheckEndPhase1(false);
				}
			}
			else if (currentGameState == ZoneType.ENTRANCE && isIntermission)
			{
				ev.AllowUse = false;
				ev.Player.Broadcast(5, "Please wait for respawns...");
			}
		}

		public void OnPlayerDie(PlayerDeathEvent ev)
		{
			// Stupid server death
			if (IsGameEnding || ev.Killer.TeamRole.Team == Smod2.API.Team.NONE) return;

			if (roundStarted)
			{
				var player = ev.Player;

				switch (currentGameState)
				{
					case ZoneType.LCZ:

						if (player.TeamRole.Team != Smod2.API.Team.SCP)
						{
							player.Broadcast(5, "You will respawn if Class D Makes it to phase 2...");
							CheckEndPhase1(true);
						}

						break;

					case ZoneType.HCZ:

						player.Broadcast(5, "You will respawn if Class D Makes it to phase 3...");
						if (player.TeamRole.Team != Smod2.API.Team.SCP)
						{
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
							player.Broadcast(5, "You were unable to escape! Please wait for the next round.");
							CheckEndPhase3(true, false);
						}
						else
						{
							CheckEndPhase3(false, true);
						}

						break;
				}
			}
		}

		public void OnSetRole(PlayerSetRoleEvent ev)
		{
			if (IsGameEnding || ev.Role == Role.SPECTATOR) return;

			// Make sure the player stays on the right team
			if (roundStarted)
			{
				var player = ev.Player;
				var playerID = player.PlayerId;
				if (ev.TeamRole.Team == Smod2.API.Team.SCP)
				{
					for (int i = RoundPlayers.Count - 1; i >= 0; i--)
					{
						if (RoundPlayers[i].PlayerId == playerID)
						{
							RemoveElevatorPlayer(playerID);
							RoundPlayers.RemoveAt(i);
							RoundSCP.Add(player);
						}
					}
				}
				else
				{
					for (int i = RoundSCP.Count - 1; i >= 0; i--)
					{
						if (RoundSCP[i].PlayerId == playerID)
						{
							RoundSCP.RemoveAt(i);
							RoundPlayers.Add(player);
						}
					}
				}
			}

			if (ev.TeamRole.Team == Smod2.API.Team.SCP)
			{
				if (currentGameState == ZoneType.UNDEFINED)
				{
					ev.Role = Role.SCP_173;
				}

				if (ev.Role == Role.SCP_079)
				{
					Timing.RunCoroutine(SetSCP079Stats(ev.Player));
				}
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
			if (IsGameEnding) return;

			switch (currentGameState)
			{
				// Make sure player can't close locked doors
				case ZoneType.LCZ:

					var door = ev.Door.GetComponent() as Door;

					if (door.doorType == 1 || door.doorType == 2)
					{
						ev.Allow = false;
					}

					break;

				case ZoneType.ENTRANCE:
				case ZoneType.HCZ:

					door = ev.Door.GetComponent() as Door;

					if (door.doorType == 2 && !door.DoorName.StartsWith("GATE"))
					{
						ev.Allow = false;
					}

					break;
			}

			// For testing
			//var door = (ev.Door.GetComponent() as Door);
			////Info(door.DoorName);
			//Info(door.doorType.ToString());
			////Info(door.name);
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
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void SCPWin() => EndGame(RoundSummary.LeadingTeam.Anomalies);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ClassDWin() => EndGame(RoundSummary.LeadingTeam.ChaosInsurgency);

		private void EndGame(RoundSummary.LeadingTeam winningTeam)
		{
			//CustomAPI.CustomAPI.Broadcast(3, $"Game Over Called. '{winningTeam}' win");
			currentGameState = ZoneType.UNDEFINED;
			roundStarted = false;
			CustomAPI.CustomAPI.EndGame(winningTeam);
		}

		public void OnRoundRestart(RoundRestartEvent ev)
		{
			roundStarted = false;
			currentGameState = ZoneType.UNDEFINED;
		}

		#endregion

		#region Phase 1

		private const float TimeUntilIntroduction = 12f;

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
			if (!IsGameEnding && currentGameState == ZoneType.LCZ) Timing.RunCoroutine(KillSCPDecontamination());
		}

		private const int KillPeanutInSeconds = 10;
		private const int PeanutHP = 3200;
		private int peanutDeconDamage;

		private IEnumerator<float> KillSCPDecontamination()
		{
			peanutDeconDamage = (int)(((PeanutHP * peanutHealthPercent) / KillPeanutInSeconds) / 4f);

			Timing.RunCoroutine(DecontaminationPlayerDamage());

			yield return Timing.WaitForSeconds(KillPeanutInSeconds + 2);

			// Unlock doors, open locked doors, open checkpoint
			var doorCount = CachedLczDoors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = CachedLczDoors[i];

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
						door.decontlock = false;
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

		/// <summary>
		/// Game can end when:
		/// Disconnect, player dies, player escapes.
		/// </summary>
		/// <param name="death">Death event hasn't updated a dead player</param>
		private void CheckEndPhase1(bool death)
		{
			if (isIntermission || IsGameEnding) return;

			var classDAlive = death ? Round.Stats.ClassDAlive - 1 : Round.Stats.ClassDAlive;

			if (classDAlive == 0)
			{
				if (PlayersReachedElevator.Count == 0)
				{
					SCPWin();
				}
				else
				{
					beginPhase2 = true;
				}
			}
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

			// Send broadcasts and lower SCP hp
			var playerCount = RoundPlayers.Count;
			for (int i = 0; i < playerCount; i++)
			{
				var player = RoundPlayers[i];
				player.Broadcast(10, "Work together to escape light containment via an elevator.");
			}

			int scpDamage = (int)(PeanutHP * (1 - peanutHealthPercent));
			if (scpDamage > 0)
			{
				playerCount = RoundSCP.Count;
				for (int i = 0; i < playerCount; i++)
				{
					var player = RoundSCP[i];
					player.Broadcast(10, "Your health has been lowered to increase your speed.");
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
			CustomAPI.CustomAPI.FlickerLights(ZoneType.LCZ);

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
			decontamination.time--;

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

		/// <summary>
		/// Check when:
		/// Player die, disconnect
		/// </summary>
		/// <param name="death">Death event hasn't updated a dead player</param>
		private void CheckEndPhase2(bool playerDeath, bool scpDeath)
		{
			if (isIntermission || IsGameEnding) return;

			var classDAlive = playerDeath ? Round.Stats.ClassDAlive - 1 : Round.Stats.ClassDAlive;
			var scpAlive = scpDeath ? Round.Stats.SCPAlive - 1 : Round.Stats.SCPAlive;

			if (classDAlive == 0)
			{
				SCPWin();
			}
			else if (scpAlive == 0)
			{
				ClassDWin();
			}
		}

		private void OnPowerReactivation() => beginPhase3 = true;

		#endregion

		#region Phase 3

		public void OnStopCountdown(WarheadStopEvent ev) => ev.Cancel = false;

		/// <summary>
		/// Check when:
		/// Player escape, player die
		/// </summary>
		/// <param name="death">Death event hasn't updated a dead player</param>
		private void CheckEndPhase3(bool playerDeath, bool scpDeath)
		{
			if (isIntermission || IsGameEnding) return;

			var classDAlive = playerDeath ? Round.Stats.ClassDAlive - 1 : Round.Stats.ClassDAlive;
			var scpAlive = scpDeath ? Round.Stats.SCPAlive - 1 : Round.Stats.SCPAlive;

			if (Server.Map.WarheadDetonated)
			{
				if (EscapedPlayers.Count > 0)
				{
					EscapeWin();
				}
				else
				{
					SCPWin();
				}
			}
			else if (classDAlive == 0)
			{
				if (EscapedPlayers.Count > 0)
				{
					EscapeWin();
				}
				else
				{
					SCPWin();
				}
			}
			else if (scpAlive == 0)
			{
				if (EscapedPlayers.Count > 0)
				{
					EscapeWin();
				}
				else
				{
					ClassDWin();
				}
			}
		}

		private void EscapeWin()
		{
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

			CustomAPI.CustomAPI.Broadcast(endScreenSpeedSeconds, messages.ToString());

			EndGame(RoundSummary.LeadingTeam.ChaosInsurgency);
		}

		public void OnDetonate() => Timing.RunCoroutine(_DetonationEndGame());
		private IEnumerator<float> _DetonationEndGame()
		{
			yield return Timing.WaitForOneFrame;

			foreach (var player in RoundPlayers)
			{
				if (player.GetCurrentRole() == Role.CLASSD)
				{
					EscapedPlayers.Add(player);
				}
			}

			CheckEndPhase3(false, false);
		}

		public void OnWarheadKeycardAccess(WarheadKeycardAccessEvent ev) => ev.Allow = false;

		private IEnumerator<float> SetSCP079Stats(Player player)
		{
			yield return Timing.WaitForSeconds(1);

			player.Scp079Data.SetCamera(Server.Map.GetRandomSpawnPoint(Role.FACILITY_GUARD));
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

		public void OnCheckEscape(PlayerCheckEscapeEvent ev)
		{
			if (IsGameEnding) return;

			ev.ChangeRole = Role.SPECTATOR;
			EscapedPlayers.Add(ev.Player);
			ev.Player.Broadcast(5, "Congratulations! You have escaped the facility!");
			CheckEndPhase3(true, false);
		}

		public void On079AddExp(Player079AddExpEvent ev) => ev.ExpToAdd *= expMultiplier;

		public void On079Door(Player079DoorEvent ev)
		{
			var doorType = (ev.Door.GetComponent() as Door).doorType;

			if (doorType == 2 || doorType == 3)
			{
				ev.Allow = false;
			}
		}

		#endregion
	}
}