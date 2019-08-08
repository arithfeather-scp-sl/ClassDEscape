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
using ArithFeather.ArithSpawningKit.IndividualSpawns;
using ArithFeather.ArithSpawningKit.RandomPlayerSpawning;
using Smod2.EventHandlers;
using ArithFeather.RandomItemSpawner;
using Smod2.EventSystem.Events;

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
		IEventHandlerGeneratorFinish, IEventHandlerPlayerDie, IEventHandlerDecideTeamRespawnQueue, IEventHandlerPlayerDropItem
	{
		public const string ModVersion = "1.04";

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

		// Item Spawning addition
		[ConfigOption] private readonly int[] onSpawnItems = new int[] { 0, 4, 9 };

		private readonly FieldInfo cachedPlayerConnFieldInfo = typeof(SmodPlayer).GetField("conn", BindingFlags.NonPublic | BindingFlags.Instance);

		//+ Round Data

		private int generatorsActivated;
		private bool roundStarted;
		private Broadcast cachedBroadcast;

		private ZoneType currentGameState;

		private List<int> playerIDhaveKeyCard;
		private List<int> PlayerIDhaveKeyCard => playerIDhaveKeyCard ?? (playerIDhaveKeyCard = new List<int>());
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

			RandomPlayerSpawning.Instance.DisablePlugin = false;

			var individualSpawns = IndividualSpawns.Instance;
			individualSpawns.DisablePlugin = false;
			individualSpawns.OnSpawnPlayer += OnSpawnPlayer;
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
			}
		}

		public void OnCallCommand(PlayerCallCommandEvent ev)
		{
			if (ev.Command == "HELP")
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
			PlayerIDhaveKeyCard.Clear();
			currentGameState = ZoneType.LCZ;
			RandomItemSpawner.RandomItemSpawner.Instance.UseDefaultEvents = false;
		}

		public void OnDecideTeamRespawnQueue(DecideRespawnQueueEvent ev)
		{
			var itemSpawner = RandomItemSpawner.RandomItemSpawner.Instance.ItemSpawning;
			var numPlayers = Server.NumPlayers;
			itemSpawner.SpawnItems(numPlayers, ZoneType.LCZ, ItemType.MAJOR_SCIENTIST_KEYCARD);
			itemSpawner.SpawnItems(numPlayers, ZoneType.LCZ, ItemType.RADIO);
			itemSpawner.SpawnItems(numPlayers, ZoneType.LCZ, ItemType.MEDKIT);
		}

		public void OnRoundStart(RoundStartEvent ev)
		{
			roundStarted = true;

			// Half all SCP173 HP
			var nuts = Server.GetPlayers(Smod2.API.Team.SCP);
			var firstNut = nuts[0];
			var damage = firstNut.GetHealth() / 2;

			firstNut.Damage(damage, DamageType.NONE);

			var nutCount = nuts.Count;
			for (int i = 1; i < nutCount; i++)
			{
				nuts[i].Damage(damage, DamageType.NONE);
			}

			// Open all locked doors
			var doors = Server.Map.GetDoors();
			var doorCount = doors.Count;
			for (int i = 0; i < doorCount; i++)
			{
				var door = doors[i].GetComponent() as Door;

				if (door.doorType != 0)
				{
					door.locked = false;
					door.NetworkisOpen = true;
				}
			}
		}

		public void OnPlayerDie(PlayerDeathEvent ev)
		{

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

					if (ev.Player.TeamRole.Team != Smod2.API.Team.SCP && PlayerIDhaveKeyCard.Contains(ev.Player.PlayerId))
					{
						var player = ev.Player;
						PlayersReachedElevator.Add(new ElevatorPlayer(player, ev.Elevator));
						player.ChangeRole(Role.SPECTATOR, false, false, false);
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

		public void OnPlayerPickupItem(PlayerPickupItemEvent ev)
		{
			switch (currentGameState)
			{
				case ZoneType.UNDEFINED:
					break;

				case ZoneType.LCZ:

					if (PlayerIDhaveKeyCard.Contains(ev.Player.PlayerId))
					{
						ev.Allow = false;
					}
					else
					{
						PlayerIDhaveKeyCard.Add(ev.Player.PlayerId);
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

		public void OnPlayerDropItem(PlayerDropItemEvent ev) => ev.Allow = false;

		// Custom Event
		private void OnSpawnPlayer(DeadPlayer deadPlayer)
		{
			deadPlayer.Player.ChangeRole(Role.CLASSD);
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

		public void OnDoorAccess(PlayerDoorAccessEvent ev)
		{
			Info((ev.Door.GetComponent() as Door).doorType.ToString());
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