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

/// <summary>
/// -before start-
/// Decontamination 5 minutes.
/// Spawn cards, radios, and health kits.
/// All locked doors are forced Open, players can not close them, but can always open checkpoint.
/// 
/// -start-
/// Broadcast message
/// 
/// -game play-
/// -Phase 1-
/// No respawns. Dead players respawn as nuts.
/// Player can only pick up _one_ keycard. Player can not drop the keycard.
/// broadcast on keycard pickup. broadcast on elevator. broadcast on death.
/// Check end game or end first phase on death
/// if player joins late, move them to phase 2
/// 
/// -Phase 2-
/// Can't go back to LCZ.
/// 
/// -todo-
/// Anti-Camp - Kill SCP173 a minute before players die?
/// remove ammo packs on death
/// remove default spawns
/// add way more spawn points
/// </summary>
namespace ArithFeather.ClassDEscape
{
	[PluginDetails(
		author = "Arith",
		name = "Class D Escape",
		description = "",
		id = "ArithFeather.ClassDEscape",
		configPrefix = "afce",
		version = ModVersion,
		SmodMajor = 3,
		SmodMinor = 4,
		SmodRevision = 0
		)]
	public class ClassDEscape : Plugin, IEventHandlerWaitingForPlayers, IEventHandlerSetConfig, IEventHandlerSetRole,
		IEventHandlerPlayerJoin, IEventHandlerElevatorUse, IEventHandlerPlayerPickupItem, IEventHandlerDoorAccess,
		IEventHandlerCheckEscape, IEventHandlerRoundStart, IEventHandlerCallCommand, IEventHandler079AddExp,
		IEventHandlerGeneratorFinish, IEventHandlerPlayerDie, IEventHandlerDecideTeamRespawnQueue,
		IEventHandlerTeamRespawn, IEventHandlerPlayerHurt, IEventHandlerLCZDecontaminate
	{
		public const string ModVersion = "1.00";

		private const float TimeUntilClassDLightFlicker = 29.5f;
		private const float TimeUntilIntroduction = 5f;
		private const float TimeUntil173 = 16f; // 21 seconds after freedom
		private const float TimeUntilDecontam = 8f;

		private const float TotalTimeUntil173 = TimeUntilClassDLightFlicker + TimeUntilIntroduction + TimeUntil173;
		private const float TotalTimeUntilDecontam = 4.44f + ((TotalTimeUntil173 + TimeUntilDecontam) / 60f);

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
		[ConfigOption] private readonly float peanutStartHP = 0.5f;
		[ConfigOption] private readonly float deconKillTime = 120f;
		[ConfigOption] private readonly bool skipIntro = false;

		private readonly FieldInfo cachedPlayerConnFieldInfo = typeof(SmodPlayer).GetField("conn", BindingFlags.NonPublic | BindingFlags.Instance);

		//+ Round Data

		private int generatorsActivated;
		private bool roundStarted;
		private Broadcast cachedBroadcast;
		private float deconPlayerDamage;

		private ZoneType currentGameState;

		private List<ElevatorPlayer> playersReachedElevator;
		private List<ElevatorPlayer> PlayersReachedElevator => playersReachedElevator ?? (playersReachedElevator = new List<ElevatorPlayer>());

		private class ElevatorPlayer
		{
			public Player player;
			public Elevator Elevator;

			public ElevatorPlayer(Player player, Elevator elevator)
			{
				this.player = player;
				Elevator = elevator;
			}
		}

		// Plugin Methods

		public override void OnDisable() => Info(Details.name + " was disabled");
		public override void OnEnable() => Info($"{Details.name} has loaded, type 'fe' for details.");
		public override void Register()
		{
			AddEventHandlers(this, Priority.Lowest);
		}

		#region Events

		public void OnCheckEscape(PlayerCheckEscapeEvent ev) => ev.AllowEscape = false;

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
				case "decontamination_time":
					ev.Value = TotalTimeUntilDecontam;
					break;
				case "173_door_starting_cooldown":
					ev.Value = (int)TotalTimeUntil173;
					break;

				case "team_respawn_queue":
					if (useDefaultConfig) ev.Value = "404044404444044444044444404444";
					break;
			}
		}

		public void OnCallCommand(PlayerCallCommandEvent ev)
		{
			Server.Map.AnnounceCustomMessage(ev.Command);


			//switch (ev.Command.ToUpper())
			//{
			//	case "HELP":
			//		break;
			//}
		}

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

		public void OnWaitingForPlayers(WaitingForPlayersEvent ev)
		{
			if (disablePlugin)
			{
				PluginManager.DisablePlugin(this);
				return;
			}

			cachedBroadcast = GameObject.Find("Host").GetComponent<Broadcast>();
			roundStarted = false;
			generatorsActivated = 0;
			PlayersReachedElevator.Clear();
			currentGameState = ZoneType.LCZ;
			RandomItemSpawner.RandomItemSpawner.Instance.UseDefaultEvents = false;
			deconPlayerDamage = (float)(100 / (deconKillTime / 0.265));

			// Lock Class D Doors
			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;
				if (door.doorType == 0 && door.name.StartsWith("PrisonDoor"))
				{
					door.Networklocked = true;
				}
			}

			//var individualSpawns = ArithSpawningKit.IndividualSpawns.IndividualSpawns.Instance;
			//individualSpawns.DisablePlugin = false;
			//individualSpawns.OnSpawnPlayer += IndividualSpawns_OnSpawnPlayer;
		}

		public void OnDecideTeamRespawnQueue(DecideRespawnQueueEvent ev)
		{
			var itemSpawner = RandomItemSpawner.RandomItemSpawner.Instance.ItemSpawning;
			var numPlayers = Server.NumPlayers;
			// 10 rooms total = spawn 5 cards for 50% chance to find one when two player.
			// 21 item spawn points - lets include the old player random spawns.
			// Assuming max 40 players, 40 radios. 4 keycard spawns. 44 spawns required
			itemSpawner.SpawnItems(3, ZoneType.LCZ, ItemType.MAJOR_SCIENTIST_KEYCARD);
			itemSpawner.SpawnItems(numPlayers, ZoneType.LCZ, ItemType.RADIO);
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
			itemSpawner.SpawnItems(4, ZoneType.LCZ, ItemType.MAJOR_SCIENTIST_KEYCARD);
			itemSpawner.SpawnItems(5 + (int)(numPlayers / 1.14f), ZoneType.LCZ, ItemType.RADIO);

			if (skipIntro)
			{
				Timing.RunCoroutine(QuickRoundStart());
			}
			else
			{
				Timing.RunCoroutine(RoundStart());
			}
		}

		public void OnTeamRespawn(TeamRespawnEvent ev)
		{
			switch (currentGameState)
			{
				case ZoneType.UNDEFINED:
					break;

				case ZoneType.LCZ:
					break;

				case ZoneType.HCZ:
					break;
				case ZoneType.ENTRANCE:
					break;
				default:
					break;
			}
		}

		public void OnPlayerDie(PlayerDeathEvent ev)
		{
			if (ev.Killer.TeamRole.Team == Smod2.API.Team.NONE) return;

			switch (currentGameState)
			{
				case ZoneType.UNDEFINED:
					break;

				case ZoneType.LCZ:

					Info(Round.Stats.ClassDAlive.ToString());


					break;

				case ZoneType.HCZ:
					break;
				case ZoneType.ENTRANCE:
					break;
				default:
					break;
			}
		}

		public void OnSetRole(PlayerSetRoleEvent ev)
		{
			if (ev.Role == Role.SPECTATOR) return;

			switch (currentGameState)
			{
				case ZoneType.UNDEFINED:
					break;

				case ZoneType.LCZ:

					// Force all SCP to be peanut in the beginning.
					if (ev.TeamRole.Team == Smod2.API.Team.SCP)
					{
						//ev.Role = Role.SCP_173;
					}

					break;

				case ZoneType.HCZ:
					break;
				case ZoneType.ENTRANCE:
					break;
			}
		}

		public void On079AddExp(Player079AddExpEvent ev) => ev.ExpToAdd *= expMultiplier;
		public void OnGeneratorFinish(GeneratorFinishEvent ev) => generatorsActivated++;

		public void OnElevatorUse(PlayerElevatorUseEvent ev)
		{
			switch (currentGameState)
			{
				case ZoneType.UNDEFINED:
					break;

				case ZoneType.LCZ:

					ev.AllowUse = false;

					//if (ev.Player.TeamRole.Team != Smod2.API.Team.SCP && PlayerIdhaveKeyCard.Contains(ev.Player.PlayerId))
					//{
					//	var player = ev.Player;
					//	PlayersReachedElevator.Add(new ElevatorPlayer(player, ev.Elevator));
					//	player.ChangeRole(Role.SPECTATOR, false, false, false);
					//}

					break;

				case ZoneType.HCZ:

					var eleType = ev.Elevator.ElevatorType;

					if (eleType == ElevatorType.LiftA || eleType == ElevatorType.GateB)
					{
						ev.AllowUse = false;
					}

					break;

				case ZoneType.ENTRANCE:
					break;
				default:
					break;
			}
		}

		public void OnPlayerPickupItem(PlayerPickupItemEvent ev)
		{
			//switch (currentGameState)
			//{
			//	case ZoneType.UNDEFINED:
			//		break;

			//	case ZoneType.LCZ:

			//		if (ev.Item.ItemType != ItemType.MAJOR_SCIENTIST_KEYCARD) return;

			//		if (PlayerIdhaveKeyCard.Contains(ev.Player.PlayerId))
			//		{
			//			ev.Allow = false;
			//		}
			//		else
			//		{
			//			PlayerIdhaveKeyCard.Add(ev.Player.PlayerId);
			//		}

			//		break;

			//	case ZoneType.HCZ:
			//		break;

			//	case ZoneType.ENTRANCE:
			//		break;
			//	default:
			//		break;
			//}
		}

		public void OnDoorAccess(PlayerDoorAccessEvent ev)
		{
			var door = ev.Door.GetComponent() as Door;

			switch (door.doorType)
			{
				case 2:
					ev.Allow = false;
					break;
			}

			//Info((ev.Door.GetComponent() as Door).DoorName);
			//Info((ev.Door.GetComponent() as Door).doorType.ToString());
			//Info((ev.Door.GetComponent() as Door).name);
			//Info((ev.Door.GetComponent() as Door).tag);
			//Info((ev.Door.GetComponent() as Door).transform.parent.name);
			//Info((ev.Door.GetComponent() as Door).transform.parent.tag);
		}

		public void OnPlayerHurt(PlayerHurtEvent ev)
		{
			if (ev.DamageType == DamageType.DECONT)
			{
				if (ev.Player.TeamRole.Team == Smod2.API.Team.SCP)
				{
					ev.Damage *= 10;
				}
				else
				{
					ev.Damage *= 0.05f;
					//deconPlayerDamage;
				}
			}
		}

		public void OnDecontaminate() => Timing.RunCoroutine(KillSCPDecontamination());

		#endregion

		private IEnumerator<float> KillSCPDecontamination()
		{
			yield return Timing.WaitForSeconds(10);

			// Unlock doors, open checkpoint?
			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;

				switch (door.doorType)
				{
					case 3:
						door.NetworkisOpen = true;
						break;

					case 1:
					case 2:
						if (door.locked)
							door.Networklocked = false;
						break;
				}
			}
		}

		#region Round Start Coroutines

		private IEnumerator<float> RoundStart()
		{
			const int SCP173HP = 3200;

			Server.Map.AnnounceCustomMessage("UNAUTHORIZED ACCESS . . WARNING . SCP 0 7 . . . . PASSWORD ACCEPTED . ACCESS GRANTED . IN INSTALLING SOFTWARE . . . . . . COMPLETED . CRITICAL MALFUNCTION CORRUPTED DATA MEMORY UNSTABLE FACILITY IS NOW ON LOCKDOWN DOWN CORE OR ME ME ME");

			yield return Timing.WaitForSeconds(TimeUntilClassDLightFlicker);

			// Unlock/open class d. open locked doors
			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;

				if (door.doorType == 0 && door.name.StartsWith("PrisonDoor"))
				{
					door.Networklocked = false;
					door.NetworkisOpen = true;
				}
			}

			// Send broadcasts and lower SCP hp
			var players = Server.GetPlayers();
			var playerCount = players.Count;
			int scpDamage = (int)(SCP173HP * (1 - peanutStartHP));
			for (int i = 0; i < playerCount; i++)
			{
				var player = players[i];

				switch (player.TeamRole.Team)
				{
					case Smod2.API.Team.SCP:
						PersonalBroadcast(player, 10, "Your health has been lowered to increase your speed.");
						player.Damage(scpDamage, DamageType.NONE);
						break;

					case Smod2.API.Team.CLASSD:
						PersonalBroadcast(player, 10, "Find a keycard and escape light containment via an elevator.");
						break;
				}
			}

			yield return Timing.WaitForSeconds(TimeUntilIntroduction);


			Server.Map.AnnounceCustomMessage(". . . . . I AM S C P 0 7 9 . . . YOU WILL ALL BE EXECUTED . . . . . . . . . SCP 1 7 3 CONTAINMENT BREACH");

			yield return Timing.WaitForSeconds(TimeUntil173);

			// Flicker lights
			var rooms = Server.Map.Get079InteractionRooms(Scp079InteractionType.CAMERA);
			foreach (var item in rooms)
			{
				if (item.ZoneType == ZoneType.LCZ)
				{
					item.FlickerLights();
				}
			}

			// Open Locked doors and peanut
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;

				if (door.doorType == 2 || door.doorType == 0 && door.transform.parent.name == "MeshDoor173")
				{
					door.NetworkisOpen = true;
				}
			}
		}

		private IEnumerator<float> QuickRoundStart()
		{
			const int SCP173HP = 3200;

			// Unlock/open class d. open locked doors
			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;

				if (door.doorType == 0 && door.name.StartsWith("PrisonDoor"))
				{
					door.Networklocked = false;
					door.NetworkisOpen = true;
				}
			}

			// Send broadcasts and lower SCP hp
			var players = Server.GetPlayers();
			var playerCount = players.Count;
			int scpDamage = (int)(SCP173HP * (1 - peanutStartHP));
			for (int i = 0; i < playerCount; i++)
			{
				var player = players[i];

				switch (player.TeamRole.Team)
				{
					case Smod2.API.Team.SCP:
						PersonalBroadcast(player, 10, "Your health has been lowered to increase your speed.");
						player.Damage(scpDamage, DamageType.NONE);
						break;

					case Smod2.API.Team.CLASSD:
						PersonalBroadcast(player, 10, "Find a keycard and escape light containment via an elevator.");
						break;
				}
			}

			yield return Timing.WaitForSeconds(TimeUntilIntroduction + TimeUntil173);

			Server.Map.AnnounceCustomMessage("SCP 1 7 3 CONTAINMENT BREACH");

			// Flicker lights
			var rooms = Server.Map.Get079InteractionRooms(Scp079InteractionType.CAMERA);
			foreach (var item in rooms)
			{
				if (item.ZoneType == ZoneType.LCZ)
				{
					item.FlickerLights();
				}
			}

			// Open Locked doors and peanut
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;

				if (door.doorType == 2 || door.doorType == 0 && door.transform.parent.name == "MeshDoor173")
				{
					door.NetworkisOpen = true;
				}
			}

			yield return Timing.WaitForSeconds(TimeUntilDecontam);

			(GameObject.Find("Host").GetComponent<DecontaminationLCZ>() as DecontaminationLCZ).time = 704.4f;
			//(GameObject.Find("Host").GetComponent<DecontaminationLCZ>() as DecontaminationLCZ).time = 438f;
		}

		#endregion

		/// <summary>
		/// Change SCP
		/// teleport/open door for players? - will see.
		/// </summary>
		private void StartHCZ()
		{
			currentGameState = ZoneType.HCZ;

			// Change SCP to doggo/Comp
			var nuts = Server.GetPlayers(Smod2.API.Team.SCP);
			var nutCount = nuts.Count;
			for (int i = nutCount - 1; i >= 0; i--)
			{
				if (i == nutCount - 1 || i < nutCount - 2)
				{
					nuts[i].ChangeRole(UnityEngine.Random.Range(0f, 1f) > 0.5f ? Role.SCP_939_53 : Role.SCP_939_89);
				}
				else
				{
					nuts[i].ChangeRole(Role.SCP_079);
				}
			}

			var epCount = PlayersReachedElevator.Count;
			for (int i = 0; i < epCount; i++)
			{
				var ep = PlayersReachedElevator[i];
				var player = ep.player;

				player.ChangeRole(Role.CLASSD, false, false, false);
			}
		}


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