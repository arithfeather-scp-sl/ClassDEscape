using Smod2;
using Smod2.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ArithFeather.CustomAPI
{
	public static class CustomAPI
	{
		private static EventHandler eventHandler;
		private static EventHandler EventHandler
		{
			get
			{
				if (eventHandler == null)
				{
					throw new Exception("Call CustomAPI.Initialize() on register first.");
				}
				return eventHandler;
			}
		}

		/// <summary>
		/// Initialize once on register
		/// </summary>
		public static void Initialize(Plugin plugin)
		{
			if (eventHandler == null)
			{
				eventHandler = new EventHandler(plugin);
				plugin.AddEventHandlers(eventHandler, Smod2.Events.Priority.High);

				EventHandler.CustomPlayerManager.OnPlayerDisconnect += (playerID) => OnPlayerDisconnect?.Invoke(playerID);
			}
			else plugin.Info("CustomAPI already initialized");
		}

		public delegate void PlayerDisconnect(int playerID);
		public static event PlayerDisconnect OnPlayerDisconnect;

		public static void FlickerLights(ZoneType zone) => eventHandler.LightControl.FlickerLights(zone);
		public static void HczLightsOn() => eventHandler.LightControl.HczLightsOn();
		public static void HczLightsOff() => eventHandler.LightControl.HczLightsOff();
		public static int LightFlickerTimer => eventHandler.LightControl.FlickerTimer;

		public static void EndGame(RoundSummary.LeadingTeam winningTeam) => eventHandler.ManualEndGame.EndGame(winningTeam);
		public static bool IsGameEnding => eventHandler.ManualEndGame.IsGameEnding;

		public static void ClearBroadcast(this Player player, int time, string message) => eventHandler.Broadcasts.PersonalClearBroadcasts(player);
		public static void ClearBroadcast() => eventHandler.Broadcasts.ClearBroadcasts();
		public static void Broadcast(this Player player, int time, string message) => eventHandler.Broadcasts.PersonalBroadcast(player, (uint)time, message);
		public static void Broadcast(int time, string message) => eventHandler.Broadcasts.Broadcast((uint)time, message);
	}

	public static class PlayerExtensions
	{
		public static Team GetCurrentTeam(this Player player)
		{
			var comp = (player.GetGameObject() as GameObject).GetComponent<CharacterClassManager>();
			if (comp != null)
			{
				return comp.klasy[comp.curClass].team;
			}
			throw new Exception("Player no longer exists");
		}
		public static Role GetCurrentRole(this Player player)
		{
			var comp = (player.GetGameObject() as GameObject).GetComponent<CharacterClassManager>();
			if (comp != null)
			{
				return (Role)comp.curClass;
			}
			throw new Exception("Player no longer exists");
		}
	}
}
