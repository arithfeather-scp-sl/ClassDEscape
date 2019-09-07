using MEC;
using Smod2;
using Smod2.API;
using Smod2.Config;
using Smod2.EventHandlers;
using Smod2.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArithFeather.ClassDEscape
{
	public class LarryController : IEventHandlerWaitingForPlayers, IEventHandlerRoundRestart, IEventHandlerLure, IEventHandlerPocketDimensionDie, IEventHandler106CreatePortal,
		IEventHandler106Teleport, IEventHandlerSetRole, IEventHandlerPlayerHurt
	{
		private Plugin plugin;
		public LarryController(Plugin plugin)
		{
			this.plugin = plugin;
			plugin.AddEventHandlers(this);
			ArithSpawningKit.RandomPlayerSpawning.RandomPlayerSpawning.Instance.DisablePlugin = false;
		}

		[ConfigOption] private readonly int secondsUntil106Teleport = 30;
		[ConfigOption] private readonly bool allowManualTeleport = true;

		[ConfigOption] private readonly bool scp106PocketKill = true;
		[ConfigOption] private readonly bool allowFemurBreaker = false;

		private static readonly Vector pocketPos = Vector.Down * 1997f;
		private static readonly Vector portalOffset = new Vector(0, -2, 0);

		private List<ScaryLarry> larries;
		private List<ScaryLarry> Larries => larries ?? (larries = new List<ScaryLarry>(3));

		public void OnSetRole(PlayerSetRoleEvent ev)
		{
			// If they were a larry, end it
			for (int i = Larries.Count - 1; i >= 0; i--)
			{
				var larry = Larries[i];

				if (larry.player.PlayerId == ev.Player.PlayerId)
				{
					larry.KillYourself();
					Larries.RemoveAt(i);
					break;
				}
			}

			if (ev.Role == Role.SCP_106)
			{
				Larries.Add(new ScaryLarry(ev.Player, allowManualTeleport, secondsUntil106Teleport));
			}
		}

		public void DisconnectPlayer(int playerID)
		{
			// If they were a larry, end it
			for (int i = Larries.Count - 1; i >= 0; i--)
			{
				var larry = Larries[i];

				if (larry.player.PlayerId == playerID)
				{
					larry.KillYourself();
					Larries.RemoveAt(i);
					return;
				}
			}
		}

		public void OnWaitingForPlayers(WaitingForPlayersEvent ev) => Larries.Clear();

		public void On106CreatePortal(Player106CreatePortalEvent ev)
		{
			foreach (var larry in Larries)
			{
				if (larry.player.PlayerId == ev.Player.PlayerId)
				{
					if (larry.AllowPortal)
					{
						var spawns = ArithSpawningKit.RandomPlayerSpawning.RandomPlayerSpawning.Instance.Data.PlayerLoadedSpawns;

						if (spawns.Count == 0)
						{
							ev.Position = ev.Player.GetPosition();
						}
						else
						{
							ev.Position = spawns[UnityEngine.Random.Range(0, spawns.Count)].Position + portalOffset;
						}
					}
					else
					{
						ev.Position = null;
					}
					return;
				}
			}
		}

		public void OnLure(PlayerLureEvent ev)
		{
			if (!allowFemurBreaker)
				ev.AllowContain = false;
		}

		public void On106Teleport(Player106TeleportEvent ev)
		{
			foreach (var larry in Larries)
			{
				if (larry.player.PlayerId == ev.Player.PlayerId)
				{
					if (!larry.AttemptTeleport())
					{
						ev.Position = null;
					}
					return;
				}
			}
		}

		public void OnPocketDimensionDie(PlayerPocketDimensionDieEvent ev)
		{
			if (!scp106PocketKill)
			{
				ev.Die = false;
				ev.Player.Teleport(pocketPos);
			}
		}

		public void OnRoundRestart(RoundRestartEvent ev)
		{
			foreach (var larry in Larries)
			{
				larry.KillYourself();
			}
		}

		public void OnPlayerHurt(PlayerHurtEvent ev)
		{
			if (!allowFemurBreaker && ev.DamageType == DamageType.LURE)
			{
				ev.Damage = 0;
			}
		}
	}
}
