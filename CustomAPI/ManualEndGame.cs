using MEC;
using Smod2;
using Smod2.API;
using Smod2.EventHandlers;
using Smod2.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ArithFeather.CustomAPI
{
	internal class ManualEndGame
	{
		private Plugin plugin;

		public ManualEndGame(Plugin plugin) => this.plugin = plugin;

		private CoroutineHandle endingCoRo;

		public bool IsGameEnding => endingCoRo.IsRunning;

		public void EndGame(RoundSummary.LeadingTeam winningTeam)
		{
			if (!IsGameEnding) endingCoRo = Timing.RunCoroutine(_EndGame(winningTeam));
		}

		private IEnumerator<float> _EndGame(RoundSummary.LeadingTeam winningTeam)
		{
			RoundSummary.SumInfo_ClassList newList = default(RoundSummary.SumInfo_ClassList);
			GameObject[] players = PlayerManager.singleton.players;

			foreach (var player in players)
			{
				if (player != null)
				{
					CharacterClassManager component = player.GetComponent<CharacterClassManager>();
					if (component.curClass >= 0)
					{
						switch (component.klasy[component.curClass].team)
						{
							case global::Team.SCP:
								if (component.curClass == 10)
								{
									newList.zombies++;
								}
								else
								{
									newList.scps_except_zombies++;
								}
								break;
							case global::Team.MTF:
								newList.mtf_and_guards++;
								break;
							case global::Team.CHI:
								newList.chaos_insurgents++;
								break;
							case global::Team.RSC:
								newList.scientists++;
								break;
							case global::Team.CDP:
								newList.class_ds++;
								break;
						}
					}
				}
			}

			var roundSum = RoundSummary.singleton;
			newList.warhead_kills = ((!AlphaWarheadController.host.detonated) ? -1 : AlphaWarheadController.host.warheadKills);
			newList.time = (int)Time.realtimeSinceStartup;
			var startClassList = roundSum.GetStartClassList();
			RoundSummary.roundTime = newList.time - startClassList.time;
			EventManager.Manager.HandleEvent<IEventHandlerRoundEnd>(new RoundEndEvent(plugin.Server, plugin.Round, newList.chaos_insurgents + newList.class_ds > 0 ? ROUND_END_STATUS.SCP_CI_VICTORY : ROUND_END_STATUS.SCP_VICTORY));
			yield return Timing.WaitForSeconds(1.5f);
			int num7 = Mathf.Clamp(ConfigFile.ServerConfig.GetInt("auto_round_restart_time", 10), 5, 1000);
			roundSum.CallRpcShowRoundSummary(startClassList, newList, winningTeam, RoundSummary.escaped_ds, RoundSummary.escaped_scientists, RoundSummary.kills_by_scp, num7);
			yield return Timing.WaitForSeconds((float)(num7 - 1));
			roundSum.CallRpcDimScreen();
			yield return Timing.WaitForSeconds(1f);
			ServerConsole.AddLog("Round restarting");
			PlayerManager.localPlayer.GetComponent<PlayerStats>().Roundrestart();
		}

		public void RoundRestartEvent() => endingCoRo.IsRunning = false;
	}
}
