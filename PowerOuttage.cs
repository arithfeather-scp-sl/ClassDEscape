using ArithFeather.CustomAPI;
using MEC;
using Smod2;
using Smod2.Config;
using Smod2.EventHandlers;
using Smod2.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ArithFeather.ClassDEscape
{
	public class PowerOuttage : IEventHandlerGeneratorFinish, IEventHandlerWaitingForPlayers, IEventHandlerRoundRestart, IEventHandlerDoorAccess
	{
		private Plugin plugin;
		public PowerOuttage(Plugin plugin)
		{
			this.plugin = plugin;
			plugin.AddEventHandlers(this);
		}

		[ConfigOption] private readonly float escapeTimerMinutes = 10f;
		[ConfigOption] private readonly float timeReductionPerGeneratorMinutes = 3f;

		private int escapeTimer;
		private int currentAnnouncement;
		private int generatorCount;
		private CoroutineHandle countdownCoroutine;

		private readonly FieldInfo genLocalTimeInfo = typeof(Generator079).GetField("localTime", BindingFlags.NonPublic | BindingFlags.Instance);

		public delegate void PowerReactivation();
		public event PowerReactivation OnPowerReactivation;

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

		public void BeginPowerOuttage()
		{
			countdownCoroutine.IsRunning = false;
			countdownCoroutine = Timing.RunCoroutine(Countdown());
		}

		private IEnumerator<float> Countdown()
		{
			CustomAPI.CustomAPI.HczLightsOff();
			escapeTimer = (int)(escapeTimerMinutes * 60);
			var announceLength = announcements.Length;
			var lastAnnouncement = announcements[announceLength - 1];
			UpdateCurrentAnnouncement(false);

			// if start time is 10 seconds over neck announcement, broadcast message
			var startAnnouncement = announcements[currentAnnouncement];
			if (announcements[currentAnnouncement].StartTime + 10 < escapeTimer)
			{
				plugin.Server.Map.AnnounceCustomMessage($"POWER REACTIVATION IN OVER {announcements[currentAnnouncement].Text}");
			}

			var countDownMessage = new StringBuilder(80);

			// Checking for countdown announcements/endings
			while (true)
			{
				if (escapeTimer >= lastAnnouncement.StartTime)
				{
					var currAnnounce = announcements[currentAnnouncement];

					if (escapeTimer == currAnnounce.StartTime)
					{
						currentAnnouncement++;

						plugin.Server.Map.AnnounceCustomMessage($"POWER REACTIVATION IN {currAnnounce.Text}");
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

					plugin.Server.Map.AnnounceCustomMessage(countDownMessage.ToString());

					yield return Timing.WaitForSeconds(escapeTimer);
					break;
				}

				yield return Timing.WaitForSeconds(1);

				escapeTimer--;
			}

			CustomAPI.CustomAPI.HczLightsOn();

			if (CustomAPI.CustomAPI.LightFlickerTimer > 2) yield return Timing.WaitForSeconds(CustomAPI.CustomAPI.LightFlickerTimer - 2);

			OnPowerReactivation?.Invoke();
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
							plugin.Server.Map.AnnounceCustomMessage($"{customA}POWER REACTIVATION IN OVER {ananas.Text}");
						}
						else
						{
							plugin.Server.Map.AnnounceCustomMessage($"{customA}POWER REACTIVATION IN LESS THEN {previousAn.Text}");
						}
					}

					return;
				}
			}
		}

		public void OnWaitingForPlayers(WaitingForPlayersEvent ev) => generatorCount = 0;

		public void OnDoorAccess(PlayerDoorAccessEvent ev)
		{
			if (!countdownCoroutine.IsRunning) return;

			if ((ev.Door.GetComponent() as Door).doorType == 3)
			{
				ev.Allow = false;

				var minutes = Mathf.FloorToInt(escapeTimer / 60);
				var seconds = escapeTimer - (minutes * 60);

				ev.Player.Broadcast(3, $"Power is down for {minutes}:{seconds}");
			}
		}

		public void OnRoundRestart(RoundRestartEvent ev) => countdownCoroutine.IsRunning = false;

		public void OnGeneratorFinish(GeneratorFinishEvent ev)
		{
			generatorCount++;

			genLocalTimeInfo.SetValue(ev.Generator.GetComponent() as Generator079, 1f);

			Timing.RunCoroutine(GeneratorActivated());
		}
		private IEnumerator<float> GeneratorActivated()
		{
			yield return Timing.WaitForSeconds(0.5f);
			yield return Timing.WaitUntilTrue(() => NineTailedFoxAnnouncer.singleton.isFree);

			if (!countdownCoroutine.IsRunning) yield break;

			escapeTimer -= (int)(timeReductionPerGeneratorMinutes * 60);
			UpdateCurrentAnnouncement(true);
		}
	}
}
