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
		IEventHandlerGeneratorFinish, IEventHandlerPlayerDie, IEventHandlerDecideTeamRespawnQueue, IEventHandlerPlayerDropItem,
		IEventHandlerTeamRespawn, IEventHandlerPlayerHurt
	{
		public const string ModVersion = "1.00";

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
		[ConfigOption] private readonly float expMultiplier = 10;
		[ConfigOption] private readonly float peanutStartHP = 0.5f;
		[ConfigOption] private readonly float deconKillTime = 120;
	
		private readonly FieldInfo cachedPlayerConnFieldInfo = typeof(SmodPlayer).GetField("conn", BindingFlags.NonPublic | BindingFlags.Instance);

		//+ Round Data

		private int generatorsActivated;
		private bool roundStarted;
		private Broadcast cachedBroadcast;
		private float deconPlayerDamage;

		private ZoneType currentGameState;

		private List<int> playerIdhaveKeyCard;
		private List<int> PlayerIdhaveKeyCard => playerIdhaveKeyCard ?? (playerIdhaveKeyCard = new List<int>());
		private List<ElevatorPlayer> playersReachedElevator;
		private List<ElevatorPlayer> PlayersReachedElevator => playersReachedElevator ?? (playersReachedElevator = new List<ElevatorPlayer>());
		private List<int> playerIdConvertedNuts;
		private List<int> PlayerIdConvertedNuts => playerIdhaveKeyCard ?? (playerIdhaveKeyCard = new List<int>());

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

				case "team_respawn_queue":
					if (useDefaultConfig) ev.Value = "404044404444044444044444404444";
					break;
				case "decontamination_time":
					if (useDefaultConfig) ev.Value = 5f;
					break;

			}
		}

		public void OnCallCommand(PlayerCallCommandEvent ev)
		{
			if (ev.Command.ToUpper() == "TEST")
			{

			}
			if (ev.Command.ToUpper() == "HELP")
			{
				ev.ReturnMessage = "";
			}
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
			PlayerIdhaveKeyCard.Clear();
			PlayerIdConvertedNuts.Clear();
			currentGameState = ZoneType.LCZ;
			RandomItemSpawner.RandomItemSpawner.Instance.UseDefaultEvents = false;
			deconPlayerDamage = (float)(100 / (deconKillTime / 0.265));

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
			// **Make sure there's enough spawn points for max players, say 40?
			// 3 * (5 + (numPlayers / 2)) = 25 spawn points for cards, 25 for radios, 25 for medkits. = 75 spawn requirement
			//
			// Other vars...
			// Decontamination time. SCP die in 5 seconds? Player die in 1 minute? Figure out how fast damage ticks are
			//var itemsToSpawn = new int[3 * (5 + (numPlayers / 2))];
			//itemSpawner.SpawnItems(, ZoneType.LCZ, ItemType.MAJOR_SCIENTIST_KEYCARD);
			//itemSpawner.SpawnItems()
		}

		public void OnRoundStart(RoundStartEvent ev)
		{
			Server.Map.AnnounceCustomMessage("SCP 1 7 3 CONTAINMENT BREACH");

			var d = GameObject.FindObjectOfType<DecontaminationLCZ>() as DecontaminationLCZ;
			Info(d.announcements[0].startTime.ToString());
			Info(d.announcements[1].startTime.ToString());
			Info(d.announcements[2].startTime.ToString());

			const int SCP173HP = 3200;

			roundStarted = true;

			// Open all locked doors
			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;

				if (door.doorType != 0)
				{
					//door.locked = false;
					door.NetworkisOpen = true;
				}
			}

			// Send broadcasts
			var players = Server.GetPlayers();
			var playerCount = players.Count;
			int scpDamage = (int)(SCP173HP * (1 - peanutStartHP));
			for (int i = 0; i < playerCount; i++)
			{
				var player = players[i];

				switch (player.TeamRole.Team)
				{
					case Smod2.API.Team.SCP:
						PersonalBroadcast(player, 10, "Snap all the necks. Protip: Hold down left-click while targeting and teleporting to automatically kill if they are in range");
						player.Damage(scpDamage, DamageType.NONE);
						break;

					case Smod2.API.Team.CLASSD:
						PersonalBroadcast(player, 10, "Find a keycard and escape light containment via an elevator. <color=Red>You must have your own keycard to escape.</color>");
						break;
				}
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

					PlayerIdConvertedNuts.Add(ev.Player.PlayerId);
					ev.Player.ChangeRole(Role.SCP_173, true, false);

					var convNuts = playerIdConvertedNuts.Count;
					var survivors = PlayersReachedElevator.Count;
					var totalPlayers = Server.NumPlayers;

					if (convNuts == totalPlayers)
					{
						Info("SCP Win");
					}
					else if (convNuts + survivors == totalPlayers)
					{
						// Start Phase 2.
						Info("Start Phase 2");
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
						ev.Role = Role.SCP_173;
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

					if (ev.Player.TeamRole.Team != Smod2.API.Team.SCP && PlayerIdhaveKeyCard.Contains(ev.Player.PlayerId))
					{
						var player = ev.Player;
						PlayersReachedElevator.Add(new ElevatorPlayer(player, ev.Elevator));
						player.ChangeRole(Role.SPECTATOR, false, false, false);
					}

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
			switch (currentGameState)
			{
				case ZoneType.UNDEFINED:
					break;

				case ZoneType.LCZ:

					if (ev.Item.ItemType != ItemType.MAJOR_SCIENTIST_KEYCARD) return;

					if (PlayerIdhaveKeyCard.Contains(ev.Player.PlayerId))
					{
						ev.Allow = false;
					}
					else
					{
						PlayerIdhaveKeyCard.Add(ev.Player.PlayerId);
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

		public void OnPlayerDropItem(PlayerDropItemEvent ev)
		{
			if (ev.Item.ItemType != ItemType.MAJOR_SCIENTIST_KEYCARD) return;
			ev.Allow = false;
		}

		public void OnDoorAccess(PlayerDoorAccessEvent ev)
		{
			var door = ev.Door.GetComponent() as Door;

			switch (door.doorType)
			{
				case 2:
					ev.Allow = false;
					break;
				case 3:
					ev.Allow = true;
					break;
			}

			Info((ev.Door.GetComponent() as Door).doorType.ToString());
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
					ev.Damage *= deconPlayerDamage;
				}
			}
		}


		// Custom event if using spawn as class d
		//private void IndividualSpawns_OnSpawnPlayer(ArithSpawningKit.IndividualSpawns.DeadPlayer player)
		//{
		//	if (player.Player.TeamRole.Role == Role.SPECTATOR)
		//	{
		//		player.Player.ChangeRole(Role.CLASSD);
		//	}
		//}

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