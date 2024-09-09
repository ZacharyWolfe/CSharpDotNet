using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oxide.Core.Plugins;
using Random = UnityEngine.Random;
using Oxide.Core;
using System;
using System.Configuration;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
	[Info ("OilRigGamemode", "Blood", "0.0.1")]
	public class OilRigGamemode : RustPlugin
	{
		private const string minicopterAssetPrefab = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
		private const string crateAssetPrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";

		public class Team
		{
			public List<BasePlayer> teamMembers;
			public List<BasePlayer> connectedMembers;
			public bool color;
			public string gamemodeQueuedFor;
			public BasePlayer identifier;
			public bool inGame;
			public ulong matchID;
			public int roundsWon;
			public int points;
			public int crateHackWins;
			public bool canForfeit;
			public bool forfeited;
		};

		public class Match
		{
			public Team a;
			public Team b;
			public int onRound;
			public int rounds;
			public bool started;
			public bool ended;
			public List<BasePlayer> teamsCombined;
			public List<BasePlayer> deathsTeamA;
			public List<BasePlayer> deathsTeamB;
			public List<BasePlayer> spectators;
			public List<BasePlayer> PenalizePlayers;
			public List<PlayerHelicopter> minicopters;
			public ulong ID;
			public string winner;
			public float timeLeft;
			public HackableLockedCrate crateKek;
			public bool increaseRoundFlag;
			public bool isRanked;
			public bool [] VehicleQueuedFor;
			public bool unloading;
			public Dictionary<BasePlayer, float> Damages;
		};

		private class PlayerElo
		{
			public float elo
			{
				get; set;
			}

			public int matchesPlayed
			{
				get; set;
			}
		}

		public class Ref<T> where T : struct
		{
			public T Value
			{
				get; set;
			}
		}

		private class Configuration
		{
			[JsonProperty ("Format")]
			public string Format { get; set; } = "{Title}: {Message}";

			[JsonProperty ("Title")]
			public string Title { get; set; } = "RustRetakes: ";

			[JsonProperty ("Title Color")]
			public string TitleColor { get; set; } = "white";

			[JsonProperty ("Message Color")]
			public string MessageColor { get; set; } = "white";

			[JsonProperty ("Chat Icon (SteamID64)")]
			public ulong ChatIcon { get; set; } = 0;
		}

		//Facepunch.Database
		private Configuration _config;
		private static Dictionary<ulong, PlayerElo> playerElo;

		List<Match> Matches = new List<Match> ();
		List<Team> teamsToRemove = new List<Team> ();

		Dictionary<Team, int> Teams = new Dictionary<Team, int> ();
		Dictionary<BasePlayer, float> DisconnectedPlayersAlert = new Dictionary<BasePlayer, float> ();
		
		Timer displayMessageTimer = null;
		Timer newTimer = null;

		[PluginReference]
		private Plugin ScraponomicsLite;

		#region ChatCommands
		[ChatCommand ("forfeit")]
		private void ForfeitCMD(BasePlayer caller){
			if (IsInMatch(caller)){
				Match match = GetMatch(caller);

				if (match.ID == 0)
					return;
				if (match.a.canForfeit && match.a.teamMembers.Contains(caller)){
					End(match);
					match.a.canForfeit = false;
				}
				else if (match.b.canForfeit){
					End(match);
					match.a.canForfeit = false;
				}
				else{
					SendReply(caller, "You don't have permission to do this.");
				}
			}
			else{
				SendReply(caller, "You don't have permission to do this.");
			}
		}
		[ChatCommand ("elo")]
		private void CmdElo (BasePlayer caller)
		{
			string chatOutput = _config.Format
				.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
				.Replace ("{Message}", $"{GetElo (caller.userID) + " <color=#ed2323><size=10>ELO</size></color>"}");
			SendReply (caller, chatOutput);
			//SendReply (caller, chatOutput);
		}

		[ChatCommand ("qleave")]
		private void LeaveQueue (BasePlayer caller)
		{
			KeyValuePair<Team, int> removeTeam = new KeyValuePair<Team, int> ();
			bool foundTeamInQueue = false;
			if (caller == null)
				return;

			if (caller == caller.Team.GetLeader ())
			{
				foreach (KeyValuePair<Team, int> entry in Teams)
				{
					if (entry.Key.identifier == caller)
					{
						removeTeam = entry;
						foundTeamInQueue = true;
						break;
					}
				}
				if (foundTeamInQueue)
				{
					Teams.Remove (removeTeam.Key);
					string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{"Successfully removed your team from the queue."}");
					SendReply (caller, chatOutput);
				}
				else
				{
					string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{"Your team is not in the queue, try queuing first."}");
					SendReply (caller, chatOutput);
				}
			}
			else
			{
				string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{"Only the Leader of your team can cancel a queue."}");
				SendReply (caller, chatOutput);
			}
		}

		[ChatCommand ("rig")]
		private void CmdRig (BasePlayer initiator)
		{
			if (initiator == null)
				return;

			if (initiator.Team == null)// || initiator.currentTeam < 1) I honestly don't know how this was working before but it should be initiator.Team.members.Count < 1 but even then we allow solos
			{
				/*
				string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{"Not a large enough group for this event. Please ensure that you are on a team."}");
				SendReply (initiator, chatOutput);
				*/
				//return;
				try
				{
					RelationshipManager.PlayerTeam PlayerTeam = RelationshipManager.ServerInstance.CreateTeam ();
					PlayerTeam.teamLeader = initiator.userID;
					PlayerTeam.AddPlayer (initiator);
				}
				catch { }
				
			}

			if (Matches.Count > 0)
			{
				string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{"Sorry, there is already an ongoing game, please try again when the other ends."}");
				SendReply (initiator, chatOutput);
				return;
			}

			if (initiator != initiator.Team.GetLeader ())
			{
				string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{"Only the team leader is allowed to initiate queuing."}");
				SendReply (initiator, chatOutput);
				return;
			}

			foreach (KeyValuePair<Team, int> check in Teams)
			{
				if (check.Key.identifier == initiator)
				{
					string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{"Your team is already in the Queue for " + check.Key.gamemodeQueuedFor}");
					SendReply (initiator, chatOutput);
					return;
				}
			}
			// Check if the person queuing is already in the queue
			if (IsInMatch (initiator))
			{
				string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{"You are already in a match, you cannot queue at this time."}");
				SendReply (initiator, chatOutput);
				return;
			}
			else
			{
				//int teamcount = 0;
				Team newTeam = new Team
				{
					teamMembers = new List<BasePlayer> (),
					connectedMembers = new List<BasePlayer> (),
					identifier = initiator,
					gamemodeQueuedFor = "",
					inGame = false
				};
				foreach (ulong teamMemberSteamID64 in initiator.Team.members)
				{
					BasePlayer teammate = BasePlayer.FindByID (teamMemberSteamID64);
					if (teammate != null && teammate.IsConnected)
					{
						string chatOutput = _config.Format
							.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
							.Replace ("{Message}", $"{"Member connected: " + teammate.displayName.ToString ()}");
						SendReply (initiator, chatOutput);
						newTeam.teamMembers.Add (teammate);
						newTeam.connectedMembers.Add (teammate);
						//teamcount++;
					}
					else if (teammate != null && !teammate.IsConnected)
					{
						initiator.Team.RemovePlayer (teammate.userID);
					}
				}
				switch (newTeam.teamMembers.Count)
				{
					case 1:
						newTeam.gamemodeQueuedFor = "Solos.";
						break;

					case 2:
						newTeam.gamemodeQueuedFor = "Duos.";
						break;

					case 3:
						newTeam.gamemodeQueuedFor = "Trios.";
						break;

					case 4:
						newTeam.gamemodeQueuedFor = "Quads.";
						break;

					default:
						string chatOutput = _config.Format
							.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
							.Replace ("{Message}", $"{"Team size invalid for this gamemode (Small Oil Rig). Err: 1 < Team size < 5."}");
						SendReply (initiator, chatOutput);
						return;
				}
				Teams.Add (newTeam, newTeam.teamMembers.Count);
				SendMessage (newTeam, "Now queuing for " + newTeam.gamemodeQueuedFor);
				if (newTimer == null)
				{
					newTimer = timer.Every (3f, () =>
					{
						if (Teams.Count > 1) // there are two teams in the queue
						{
							KeyValuePair<Team, int> team = Teams.First ();
							KeyValuePair<Team, int> entry = new KeyValuePair<Team, int> ();
							foreach (KeyValuePair<Team, int> check in Teams)
							{
								if (check.Key.identifier != team.Key.identifier && team.Value == check.Value)
								{
									teamsToRemove.Add (team.Key);
									teamsToRemove.Add (check.Key);
									entry = check;
									StartGame (ref team, ref entry);
									if (newTimer != null)
									{
										newTimer.Destroy ();
										newTimer = null;
									}
									break;
								}
							}
							foreach (Team remove in teamsToRemove)
							{
								Teams.Remove (remove);
							}
							teamsToRemove.Clear ();
						}
					});
					displayMessageTimer = timer.Every (20f, () =>
					{
						if (Teams.ContainsKey (newTeam))
						{
							string chatOutput = _config.Format
								.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
								.Replace ("{Message}", $"{"You are in the Queue for " + newTeam.gamemodeQueuedFor}");
							SendReply (initiator, chatOutput);
						}
						else
						{
							displayMessageTimer.Destroy ();
							displayMessageTimer = null;
						}
					});
				}
			}
		}
		#endregion

		private void StartGame (ref KeyValuePair<Team, int> teamA, ref KeyValuePair<Team, int> teamB)
		{
			bool selectedTeamColor = Random.Range (0, 2) == 1;
			bool selectedEntryColor = !selectedTeamColor;

			Team teamANew = new Team
			{
				color = selectedTeamColor,
				gamemodeQueuedFor = teamA.Key.gamemodeQueuedFor,
				matchID = teamA.Key.matchID,
				identifier = teamA.Key.identifier,
				inGame = teamA.Key.inGame,
				teamMembers = teamA.Key.teamMembers,
				connectedMembers = teamA.Key.connectedMembers,
				roundsWon = 0
			};

			Team teamBNew = new Team
			{
				color = selectedEntryColor,
				gamemodeQueuedFor = teamB.Key.gamemodeQueuedFor,
				matchID = teamB.Key.matchID,
				identifier = teamB.Key.identifier,
				inGame = teamB.Key.inGame,
				teamMembers = teamB.Key.teamMembers,
				connectedMembers = teamB.Key.connectedMembers,
				roundsWon = 0
			};

			KeyValuePair<Team, int> teamAKVP = new KeyValuePair<Team, int> (teamANew, teamANew.teamMembers.Count);
			KeyValuePair<Team, int> teamBKVP = new KeyValuePair<Team, int> (teamBNew, teamBNew.teamMembers.Count);

			teamANew.inGame = true;
			teamBNew.inGame = true;

			Match newMatch = new Match
			{
				a = teamANew,
				b = teamBNew,
				started = false,
				onRound = 1,
				rounds = 6,
				ended = false,
				ID = teamANew.identifier.userID + teamBNew.identifier.userID, // Unique key based on the leaders of both teams' game IDs
				deathsTeamA = new List<BasePlayer> (),
				deathsTeamB = new List<BasePlayer> (),
				spectators = new List<BasePlayer> (),
				teamsCombined = new List<BasePlayer> (),
				PenalizePlayers = new List<BasePlayer> (),
				minicopters = new List<PlayerHelicopter> (),
				VehicleQueuedFor = new bool [4]
			};

			if (!newMatch.started)
			{
				if (newMatch.a.color)
				{
					SendMessage (newMatch.a, "You are on the <color=#316bf5>BLUE</color> team.");
					SendMessage (newMatch.a, "You must wait 7.5 seconds before <color=#ed2323>DEFENDING</color> the OilRig from your opponent.");
				}
				else
				{
					SendMessage (newMatch.a, "You are on the <color=#ed2323>RED</color> team.");
					SendMessage (newMatch.a, "Get to the Oil Rig and successfully <color=#ed2323>ELIMINATE</color> all enemies before the 5 minute timer runs out.");
				}
				if (!newMatch.b.color)
				{
					SendMessage (newMatch.b, "You are on the <color=#ed2323>RED</color> team.");
					SendMessage (newMatch.b, "Get to the Oil Rig and successfully <color=#ed2323>ELIMINATE</color> all enemies before the 5 minute timer runs out.");
				}
				else
				{
					SendMessage (newMatch.b, "You are on the <color=#316bf5>BLUE</color> team.");
					SendMessage (newMatch.b, "You must wait 7.5 seconds before <color=#ed2323>DEFENDING</color> the OilRig from your opponent.");
				}
				newMatch.started = true;
			}
			var objec = newMatch;
			objec.crateKek = StartTimedCrate (newMatch);
			//PlayLockedCrate (newMatch.crateKek.transform.position);
			newMatch = objec;
			newMatch.teamsCombined = CombineTeams (newMatch.a, newMatch.b);

			string team1 = "";
			string team2 = "";

			for (int i = 0; i < teamA.Key.teamMembers.Count - 1; i++)
			{
				team1 = team1 + teamA.Key.teamMembers [i].displayName.ToString () + ", ";
			}

			for (int j = 0; j < teamB.Key.teamMembers.Count - 1; j++)
			{
				team2 = team2 + teamB.Key.teamMembers [j].displayName.ToString () + ", ";
			}
			if (teamA.Key.teamMembers.Count == 1)
			{
				team1 += teamA.Key.teamMembers [teamA.Key.teamMembers.Count - 1].displayName.ToString ();
			}
			else
			{
				team1 += teamA.Key.teamMembers [teamA.Key.teamMembers.Count - 1].displayName.ToString () + ".";
			}
			if (teamB.Key.teamMembers.Count == 1)
			{
				team2 += teamB.Key.teamMembers [teamB.Key.teamMembers.Count - 1].displayName.ToString ();
			}
			else
			{
				team2 += teamB.Key.teamMembers [teamB.Key.teamMembers.Count - 1].displayName.ToString () + ".";
			}
			

			if (teamAKVP.Key.teamMembers.Count == 1)
			{
				SendToGame (teamAKVP, teamANew.color, newMatch);
				SendMessage (teamAKVP.Key, "You are facing, " + team2 + " at " +  GetElo(teamBKVP.Key.identifier.userID) + " <color=#ed2323><size=10>ELO</size></color>");

				SendToGame (teamBKVP, teamBNew.color, newMatch);
				SendMessage (teamBKVP.Key, "You are facing, " + team1 + " at " + GetElo (teamBKVP.Key.identifier.userID) + " <color=#ed2323><size=10>ELO</size></color>");
			}
			else
			{
				float combinedEloA = 0;
				float combinedEloB = 0;
				foreach (BasePlayer teammate in teamAKVP.Key.teamMembers)
				{
					combinedEloA += GetElo (teammate.userID);
				}
				foreach (BasePlayer teammate2 in teamBKVP.Key.teamMembers)
				{
					combinedEloB += GetElo (teammate2.userID);
				}
				combinedEloA /= teamAKVP.Key.teamMembers.Count;
				combinedEloB /= teamBKVP.Key.teamMembers.Count;

				SendToGame (teamAKVP, teamANew.color, newMatch);
				SendMessage (teamAKVP.Key, "You are facing, " + team2 + " with an average of " + combinedEloB + " <color=#ed2323><size=10>ELO</size></color>");

				SendToGame (teamBKVP, teamBNew.color, newMatch);
				SendMessage (teamBKVP.Key, "You are facing, " + team1 + " with an average of " + combinedEloA + " <color=#ed2323><size=10>ELO</size></color>");
			}
			
			Matches.Add (newMatch);
			//var match = new Ref<Match> { Value = newMatch};
			//matchRefList.Add (match);
			CheckGame (newMatch);
		}

		/*
		private void RoundIncrease (Match match)
		{
			Server.Broadcast ("In round increase");
			if (match.ID == 0)
			{
				return;
			}
			Team teamANew = new Team
			{
				color = match.a.color,
				gamemodeQueuedFor = match.a.gamemodeQueuedFor,
				matchID = match.a.matchID,
				identifier = match.a.identifier,
				inGame = match.a.inGame,
				teamMembers = match.a.teamMembers,
				connectedMembers = match.a.connectedMembers,
				roundsWon = match.a.roundsWon
			};

			if (match.a.color) // if the A team is blue
			{
				Server.Broadcast ("Increasing roundwon of team blue " + teamANew.roundsWon);
				var objec = teamANew;
				objec.roundsWon++;
				teamANew = objec;
				Server.Broadcast ("after increasing roundwon of team blue " + teamANew.roundsWon);
			}

			Team teamBNew = new Team
			{
				color = match.b.color,
				gamemodeQueuedFor = match.b.gamemodeQueuedFor,
				matchID = match.b.matchID,
				identifier = match.b.identifier,
				inGame = match.b.inGame,
				teamMembers = match.b.teamMembers,
				connectedMembers = match.b.connectedMembers,
				roundsWon = match.b.roundsWon
			};

			if (match.b.color)
			{
				Server.Broadcast ("Increasing roundwon of team blue " + teamBNew.roundsWon);
				var objec = teamBNew;
				objec.roundsWon++;
				teamBNew = objec;
				Server.Broadcast ("after increasing roundwon of team blue " + teamBNew.roundsWon);
			}

			//KeyValuePair<Team, int> teamAKVP = new KeyValuePair<Team, int> (teamANew, teamANew.teamMembers.Count);
			//KeyValuePair<Team, int> teamBKVP = new KeyValuePair<Team, int> (teamBNew, teamBNew.teamMembers.Count);

			Match match2 = new Match {
				a = teamANew,
				b = teamBNew,
				deathsTeamA = match.deathsTeamA,
				deathsTeamB = match.deathsTeamB,
				crateKek = match.crateKek,
				teamsCombined = match.teamsCombined,
				ended = match.ended,
				ID = match.ID,
				minicopters = match.minicopters,
				onRound = match.onRound,
				PenalizePlayers = match.PenalizePlayers,
				rounds = match.rounds,
				spectators = match.spectators,
				started = match.started,
				timeLeft = match.timeLeft,
				winner = match.winner,
				increaseRoundFlag = true
			};

			if (teamANew.color)
			{
				SendMessage (teamANew, "Your team successfully won the round by letting the crate time expire. ");
				SendMessage (teamBNew, "Your team did not successfully eliminate all players before the time expired. ");
			}
			else
			{
				SendMessage (teamBNew, "Your team successfully won the round by letting the crate time expire. ");
				SendMessage (teamANew, "Your team did not successfully eliminate all players before the time expired. ");
			}

			Server.Broadcast ("Checking match in roundincrease with flag of " + match2.increaseRoundFlag);
			CheckGame (match2);
		}
		*/

		private void SendToGame (KeyValuePair<Team, int> team, bool color, Match match)
		{

			Vector3 [] rigSpawns = new Vector3 [4];
			Vector3 outsideSpawn;

			rigSpawns [0].x = -323.398f;
			rigSpawns [0].y = 22.4263f;
			rigSpawns [0].z = -364.819f;

			rigSpawns [1].x = -334.93f;
			rigSpawns [1].y = 22.72f;
			rigSpawns [1].z = -363.82f;

			rigSpawns [2].x = -326.21f;
			rigSpawns [2].y = 17.93f;
			rigSpawns [2].z = -360.49f;

			rigSpawns [3].x = -348.74f;
			rigSpawns [3].y = 30.79f;
			rigSpawns [3].z = -368.81f;

			outsideSpawn.x = -426.597f;
			outsideSpawn.y = 20.0818f;
			outsideSpawn.z = -238.781f;

			int [] itemIDsWear = { -194953424, 1110385766, 1850456855, -1549739227, 1366282552 };
			List<int> index = new List<int> ();
			foreach (BasePlayer teamMember in team.Key.teamMembers)
			{
				if (teamMember == null)
					continue;
				PlayerInventory Inventory = teamMember.inventory;

				if (teamMember.isMounted)
				{
					teamMember.EnsureDismounted ();
				}
				if (!Inventory.containerWear.IsLocked ())
				{
					Inventory.containerWear.SetLocked (true);
				}

				Inventory.Strip ();
				if (color)
				{
					int indicie = Random.Range (0, 4);
					while (!index.Contains(indicie))
					{
						indicie = Random.Range (0, 4);
						index.Add (indicie);
					}

					Teleport (teamMember, rigSpawns [indicie]);
					timer.Repeat (0.125f, 56, () =>
					{
						bool move = (teamMember.transform.position.x >= rigSpawns [indicie].x + 3.0f ||
									 teamMember.transform.position.z >= rigSpawns [indicie].z + 3.0f) ||

									(teamMember.transform.position.x <= rigSpawns [indicie].x - 3.0f ||
									 teamMember.transform.position.z <= rigSpawns [indicie].z - 3.0f) ||

									(teamMember.transform.position.x >= rigSpawns [indicie].x + 3.0f ||
									 teamMember.transform.position.z <= rigSpawns [indicie].z - 3.0f) ||

									(teamMember.transform.position.x <= rigSpawns [indicie].x - 3.0f ||
									 teamMember.transform.position.z >= rigSpawns [indicie].z + 3.0f);

						if (teamMember != null && move)
							teamMember.MovePosition (rigSpawns [indicie]);
					});
				}
				else
				{
					Vector3 newSpawn;
					newSpawn.x = outsideSpawn.x;
					newSpawn.y = outsideSpawn.y + 25.0f;
					newSpawn.z = outsideSpawn.z;

					Teleport (teamMember, newSpawn);
					teamMember.UpdateNetworkGroup ();
					teamMember.UpdateSurroundings ();
				}

				GiveKit (teamMember, color);
				Metabolize (teamMember);
			}
			if (!color)
			{
				OnEntitySpawned (team.Key, match);
			}
			index.Clear ();
		}

		private HackableLockedCrate StartTimedCrate (Match match)
		{
			Vector3 cratePos;
			cratePos.x = -322.63f;
			cratePos.y = 27.18f;
			cratePos.z = -342.04f;

			const float REQUIREDHACKSECONDS = 30f;
			HackableLockedCrate lockedCrate;
			lockedCrate = (HackableLockedCrate) GameManager.server.CreateEntity (crateAssetPrefab, cratePos, match.a.identifier.ServerRotation);

			if (lockedCrate != null)
			{
				lockedCrate.SpawnAsMapEntity ();
				lockedCrate.StartHacking ();
				lockedCrate.shouldDecay = false;
				lockedCrate.enableSaving = false;
				lockedCrate.hackSeconds = HackableLockedCrate.requiredHackSeconds - REQUIREDHACKSECONDS;
				return lockedCrate;
			}
			return lockedCrate;
		}
		private void CheckGame (Match match)
		{
			match.Damages = new Dictionary<BasePlayer, float> ();

			Timer myTimer = null;
			
			List<BasePlayer> removeFromTeamA = new List<BasePlayer> ();
			List<BasePlayer> removeFromTeamB = new List<BasePlayer> ();
			myTimer = timer.Every (1f, () =>
			{
				if (match.unloading)
				{
					if (myTimer != null)
					{
						myTimer.Destroy ();
						myTimer = null;
					}
				}
				match.timeLeft++;
				foreach (BasePlayer teammate in match.a.teamMembers)
				{
					if (teammate != null && teammate.IsConnected && teammate.IsValid())
					{
						match.a.connectedMembers.Add (teammate);
					}
					else if (teammate != null && !teammate.IsConnected && teammate.IsValid () && match.a.teamMembers.Count != 1)
					{
						SwapLeadership(teammate);
						removeFromTeamA.Add (teammate);
						match.a.connectedMembers.Remove (teammate);
						if (match.isRanked)
						{
							match.PenalizePlayers.Add (teammate);
							SendMessage (match.a, "Your teammate, " + teammate.displayName.ToString () + " has abandoned the match. Your team is now available to /forfeit and will be punished accordingly.");
							match.a.canForfeit = true;
						}
						else
						{
							SendMessage (match.a, teammate + " has abandoned the match.");
						}
					}
				}
				foreach (BasePlayer teammate2 in match.b.teamMembers)
				{
					if (teammate2 != null && teammate2.IsConnected && teammate2.IsValid () && !match.b.connectedMembers.Contains (teammate2))
					{
						match.b.connectedMembers.Add (teammate2);
					}
					else if (teammate2 != null && !teammate2.IsConnected && teammate2.IsValid () && match.a.teamMembers.Count != 1)
					{
						SwapLeadership(teammate2);
						removeFromTeamB.Add (teammate2);
						match.b.connectedMembers.Remove (teammate2);
						if (match.isRanked)
						{
							match.PenalizePlayers.Add (teammate2);
							SendMessage (match.b, "Your teammate, " + teammate2.displayName.ToString () + " has abandoned the match. Your team is now available to /forfeit and will be punished accordingly.");
							match.PenalizePlayers.Add (teammate2);
							match.b.canForfeit = true;
						}
						else if (teammate2 != null && !teammate2.IsConnected && teammate2.IsValid ())
						{
							SendMessage (match.b, teammate2 + " has abandoned the match.");
						}
					}
				}
				foreach (BasePlayer leaver in removeFromTeamA)
				{
					match.a.teamMembers.Remove (leaver);
				}
				removeFromTeamA.Clear ();
				foreach (BasePlayer leaver2 in removeFromTeamB)
				{
					match.b.teamMembers.Remove (leaver2);
				}
				removeFromTeamB.Clear ();
				//match.a.identifier.Team.
				if (match.timeLeft >= 30f && match.rounds == match.onRound)
				{
					SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
					SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
					match.b.roundsWon += 1;
					End (match);
					if (myTimer != null)
					{
						myTimer.Destroy ();
						myTimer = null;
					}
				}
				else if (match.timeLeft >= 30f && match.onRound < match.rounds)
				{
					if (match.b.color)
					{
						Team teamBlue = new Team
						{
							color = match.b.color,
							matchID = match.b.matchID,
							gamemodeQueuedFor = match.b.gamemodeQueuedFor,
							identifier = match.b.identifier,
							inGame = match.b.inGame,
							teamMembers = match.b.teamMembers,
							connectedMembers = match.b.connectedMembers,
							roundsWon = match.b.roundsWon + 1
						};
						SendMessage (teamBlue, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
						SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
						match.b = teamBlue;
					}
					else
					{
						Team teamRED = new Team
						{
							color = match.a.color,
							matchID = match.a.matchID,
							gamemodeQueuedFor = match.a.gamemodeQueuedFor,
							identifier = match.a.identifier,
							inGame = match.a.inGame,
							teamMembers = match.a.teamMembers,
							connectedMembers = match.a.connectedMembers,
							roundsWon = match.a.roundsWon + 1
						};
						SendMessage (teamRED, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
						SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
						match.a = teamRED;
					}
					match.onRound++;
					if (match.b.roundsWon == 3 || match.a.roundsWon == 3)
					{
						End (match);
						if (myTimer != null)
						{
							myTimer.Destroy ();
							myTimer = null;
						}
					}
					else
					{
						Reset (match);
						match.timeLeft = 0;
						match.ended = false;
					}
				}
				else
				{
					if (match.rounds == match.onRound && (match.deathsTeamA.Count >= match.a.teamMembers.Count || match.deathsTeamB.Count >= match.b.teamMembers.Count))
					{
						if (match.deathsTeamA.Count >= match.a.teamMembers.Count)
						{
							match.b.roundsWon += 1;
						}
						else if (match.deathsTeamB.Count >= match.b.teamMembers.Count)
						{
							match.a.roundsWon += 1;
						}

						End (match);
						if (myTimer != null)
						{
							myTimer.Destroy ();
							myTimer = null;
						}
					}
					else if (match.deathsTeamB.Count >= match.b.teamMembers.Count || match.deathsTeamA.Count >= match.a.teamMembers.Count)
					{
						if (match.deathsTeamA.Count >= match.a.teamMembers.Count)
						{
							Team teamB = new Team
							{
								color = match.b.color,
								matchID = match.b.matchID,
								gamemodeQueuedFor = match.b.gamemodeQueuedFor,
								identifier = match.b.identifier,
								inGame = match.b.inGame,
								teamMembers = match.b.teamMembers,
								connectedMembers = match.b.connectedMembers,
								roundsWon = match.b.roundsWon + 1
							};
							if (match.b.color)
							{
								SendMessage (teamB, "<color=#316bf5>BLUE</color> won the round.");
								SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round.");
							}
							else
							{
								SendMessage (teamB, "<color=#ed2323>RED</color> won the round.");
								SendMessage (match.a, "<color=#ed2323>RED</color> won the round.");
							}
							match.b = teamB;
						}
						else
						{
							Team teamA = new Team
							{
								color = match.a.color,
								matchID = match.a.matchID,
								gamemodeQueuedFor = match.a.gamemodeQueuedFor,
								identifier = match.a.identifier,
								inGame = match.a.inGame,
								teamMembers = match.a.teamMembers,
								connectedMembers = match.a.connectedMembers,
								roundsWon = match.a.roundsWon + 1
							};
							if (match.a.color)
							{
								SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round.");
								SendMessage (teamA, "<color=#316bf5>BLUE</color> won the round.");
							}
							else
							{
								SendMessage (match.b, "<color=#ed2323>RED</color> won the round.");
								SendMessage (teamA, "<color=#ed2323>RED</color> won the round.");
							}
							match.a = teamA;
						}
						match.onRound++;
						match.timeLeft = 0;
						match.ended = false;
						if (match.b.roundsWon == 3 || match.a.roundsWon == 3)
						{
							End (match);
							if (myTimer != null)
							{
								myTimer.Destroy ();
								myTimer = null;
							}
						}
						else
						{
							Reset (match);
						}
					}
					match.ended = false;
					//if (onlineConnectionsTeamA.Count != match.a.identifier.Team.members.Count)
					//{
					//passengers = teamList.Except (drivers).ToList ();
					//	removePlayersTeamA = match.a.identifier.Team.members.Except (onlineConnectionsTeamA.Values).ToList ();
					//}
					//if (onlineConnectionsTeamB.Count != match.b.identifier.Team.members.Count)
					//{
					//passengers = teamList.Except (drivers).ToList ();
					//	removePlayersTeamB = match.b.identifier.Team.members.Except (onlineConnectionsTeamB.Values).ToList ();
					//}
				}
			});
		}

		private void Reset (Match match)
		{
			foreach (KeyValuePair<BasePlayer, float> KVP in match.Damages)
			{
				SendMessage (match.a, KVP.Value.ToString() + " dealt by " + KVP.Key.displayName.ToString());
				SendMessage (match.b, KVP.Value.ToString () + " dealt by " + KVP.Key.displayName.ToString ());
			}
			if (match.ID == 0)
			{
				return;
			}

			var obj = match;
			//match.crateKek = StartTimedCrate (match);
			obj.increaseRoundFlag = false;


			//AwardPoints (match);
			List<PlayerHelicopter> MinicoptersToRemove = new List<PlayerHelicopter> ();
			match.increaseRoundFlag = false;
			match.deathsTeamB.Clear ();
			match.deathsTeamA.Clear ();
			match.spectators.Clear ();

			if (match.crateKek != null)
			{
				match.crateKek.Kill ();
				match.crateKek = StartTimedCrate (match);
			}

			if (match.minicopters != null)
			{
				foreach (PlayerHelicopter mini in match.minicopters)
				{
					if (mini != null && !mini.IsDestroyed)
					{
						mini.Kill ();
						if (match.minicopters != null)
							MinicoptersToRemove.Add (mini);
					}
				}
				foreach (PlayerHelicopter minicopter in MinicoptersToRemove)
				{
					match.minicopters.Remove (minicopter);
				}
			}
			if (!match.ended)
			{
				Respawn (match);
			}
			MinicoptersToRemove.Clear ();
			match.Damages.Clear ();
		}

		private void Respawn (Match match)
		{
			KeyValuePair<Team, int> teamA = new KeyValuePair<Team, int> (match.a, match.a.teamMembers.Count);
			KeyValuePair<Team, int> teamB = new KeyValuePair<Team, int> (match.b, match.b.teamMembers.Count);
			match.deathsTeamA.Clear ();
			match.deathsTeamB.Clear ();
			match.spectators.Clear ();
			SendToGame (teamA, match.a.color, match);
			SendToGame (teamB, match.b.color, match);
		}

		#region Helper Functions
		private List<BasePlayer> CombineTeams (Team a, Team b)
		{
			List<BasePlayer> players = new List<BasePlayer> ();
			foreach (BasePlayer player in a.teamMembers)
			{
				if (player != null)
					players.Add (player);
			}
			foreach (BasePlayer player2 in b.teamMembers)
			{
				if (player2 != null)
					players.Add (player2);
			}
			return players;
		}

		public void GiveKit (BasePlayer player, bool teamColor)
		{

			PlayerInventory Inventory = player.inventory;

			Inventory.Strip ();
			Item item = ItemManager.CreateByItemID (1545779598, 1);
			var weapon = item.GetHeldEntity () as BaseProjectile;
			if (weapon != null)
			{
				weapon.primaryMagazine.contents = 0; // unload the old ammo
				Item holosight = ItemManager.CreateByItemID (442289265, 1);
				holosight.MoveToContainer (item.contents);

				// Add Laser to the assault rifle
				Item laser = ItemManager.CreateByItemID (-132516482, 1);
				laser.MoveToContainer (item.contents);
				weapon.SendNetworkUpdateImmediate (false); // update
				weapon.primaryMagazine.contents = weapon.primaryMagazine.capacity; // load new ammo
			}
			GiveItems (Inventory, 1, Inventory.containerWear);                                               // ARMOR
			if (teamColor)
			{
				Inventory.GiveItem (ItemManager.CreateByItemID (1751045826, 1, 14178), Inventory.containerWear);                // HOODIE
				Inventory.GiveItem (ItemManager.CreateByItemID (237239288, 1, 10001), Inventory.containerWear);                 // PANTS
			}
			else
			{
				Inventory.GiveItem (ItemManager.CreateByItemID (1751045826, 1), Inventory.containerWear);                       // HOODIE
				Inventory.GiveItem (ItemManager.CreateByItemID (237239288, 1, 3099244148), Inventory.containerWear);            // PANTS
			}
			Inventory.GiveItem (item);                                                                                          // ASSAULT RIFLE
			Inventory.GiveItem (ItemManager.CreateByItemID (-1211166256, 128), Inventory.containerMain);                        // AMMO
			Inventory.GiveItem (ItemManager.CreateByItemID (-1211166256, 28), Inventory.containerMain);                         // AMMO
			Inventory.GiveItem (ItemManager.CreateByItemID (1079279582, 2), Inventory.containerBelt);                           // SYRINGE
			Inventory.GiveItem (ItemManager.CreateByItemID (1079279582, 2), Inventory.containerBelt);                           // SYRINGE
			Inventory.GiveItem (ItemManager.CreateByItemID (1079279582, 2), Inventory.containerBelt);                           // SYRINGE
			Inventory.GiveItem (ItemManager.CreateByItemID (-2072273936, 3), Inventory.containerBelt);                          // BANDAGE
			Inventory.GiveItem (ItemManager.CreateByItemID (-2072273936, 3), Inventory.containerBelt);                          // BANDAGE
		}

		private void SendMessage (Team team, string message)
		{
			foreach (BasePlayer player in team.teamMembers)
			{
				if (player != null)
				{
					string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{message}");
					SendReply (player, chatOutput);
				}
			}
		}

		private void GiveItems (PlayerInventory teamMemberInventory, int amount, ItemContainer whereOnPlayer)
		{
			int [] itemIDsWear = { -194953424, 1110385766, 1850456855, -1549739227, 1366282552 };
			foreach (int itemNumber in itemIDsWear)
			{
				teamMemberInventory.GiveItem (ItemManager.CreateByItemID (itemNumber, amount), whereOnPlayer);
			}
		}

		private void PlayCodeLock (Vector3 position)
		{
			Effect.server.Run ("assets/prefabs/locks/keypad/effects/lock.code.lock.prefab", position);
		}

		private void PlayCodeUnlock (Vector3 position)
		{
			Effect.server.Run ("assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab", position);
		}

		private void PlayLockedCrate (Vector3 position)
		{
			Effect.server.Run ("assets/prefabs/io/electric/other/alarmsound.prefab", position);
		}

		public void Metabolize (BasePlayer player)
		{
			if (player == null)
				return;

			player.health = 100f;
			player.metabolism.temperature.min = 32f;
			player.metabolism.temperature.max = 32f;
			player.metabolism.temperature.value = 32f;
			player.metabolism.poison.value = 0f;
			player.metabolism.calories.value = player.metabolism.calories.max;
			player.metabolism.hydration.value = player.metabolism.hydration.max;
			player.metabolism.wetness.max = 0f;
			player.metabolism.wetness.value = 0f;
			player.metabolism.radiation_level.max = 0f;
			player.metabolism.radiation_poison.max = 0f;

			player.metabolism.SendChangesToClient ();
		}

		public void DeMetabolize (BasePlayer player)
		{
			if (player == null)
				return;

			player.health = 53f;
			player.metabolism.calories.SetValue (81);
			player.metabolism.hydration.Reset ();
			player.metabolism.SendChangesToClient ();
		}

		public void Teleport (BasePlayer player, Vector3 destination)
		{
			if (player == null || destination == Vector3.zero)
			{
				return;
			}

			if (player.IsConnected && player.IsValid ())
			{
				player.Invoke (player.EndLooting, 0.01f);

				if (player.IsWounded ())
				{
					player.StopWounded ();
				}
				player.metabolism.bleeding.Set (0);

				if (player.IsSleeping ())
				{
					player.EndSleeping ();
				}

				if (player.isMounted)
				{
					player.EnsureDismounted ();
				}

				player.Server_CancelGesture ();

				Metabolize (player);

				player.Teleport (destination);

				if (player.net?.connection == null)
					return;

				try
				{
					player.ClearEntityQueue (null);
				}
				catch
				{
				}

				player.SetPlayerFlag (BasePlayer.PlayerFlags.ReceivingSnapshot, true);
				player.ClientRPCPlayer (null, player, "StartLoading", arg1: true);
				player.UpdateNetworkGroup ();
				player.SendNetworkUpdateImmediate (false);
				player.ClearEntityQueue (null);
				player.SendFullSnapshot ();
				player.SendEntityUpdate ();
			}

			/*
			if (player == null || destination == Vector3.zero)
			{
				return;
			}

			player.Invoke (player.EndLooting, 0.01f);

			if (player.IsWounded ())
			{
				player.StopWounded ();
			}

			if (player.IsSleeping ())
			{
				player.EndSleeping ();
			}
			
			//player.Respawn ();
			Metabolize (player);

			player.Teleport (destination);

			if (player.IsConnected && (Vector3.Distance (player.transform.position, destination) > 50f))
			{
				player.SetPlayerFlag (BasePlayer.PlayerFlags.ReceivingSnapshot, true);
				player.ClientRPCPlayer (null, player, "StartLoading");
				player.UpdateNetworkGroup ();
				player.SendEntityUpdate ();
			}
			*/
		}
		void OnEntitySpawned (Team team, Match match)
		{
			List<BasePlayer> teamList = new List<BasePlayer> ();
			List<BasePlayer> drivers = new List<BasePlayer> ();
			List<BasePlayer> passengers = new List<BasePlayer> ();
			List<int> randIntList = new List<int> ();

			int miniCount = 0;

			if (team.teamMembers.Count % 2 == 0)
			{
				miniCount = team.teamMembers.Count / 2;
			}
			else
			{
				miniCount = (team.teamMembers.Count / 2) + 1;
			}
			Vector3 [] newPositions = new Vector3 [miniCount];
			for (float i = 0; i < miniCount; i += 1)
			{
				Vector3 newPos;
				newPos.x = (team.identifier.transform.position.x + (3.5f * i));
				newPos.y = team.identifier.transform.position.y;
				newPos.z = team.identifier.transform.position.z;
				newPositions [((int) i)] = newPos;
			}
			foreach (BasePlayer player in team.teamMembers)
			{
				if (player.IsConnected && player != null)
					teamList.Add (player);
			}
			for (int i = 0; i < teamList.Count; i++)
			{
				int randInt = Random.Range (0, teamList.Count);
				randIntList.Add (randInt);
				drivers.Add (teamList [randIntList [i]]);
			}
			passengers = teamList.Except (drivers).ToList ();

			for (int i = 0; i < miniCount; i++)
			{
				PlayerHelicopter mini = team.identifier.GetComponentInParent<PlayerHelicopter> ();

				mini = GameManager.server.CreateEntity (minicopterAssetPrefab, newPositions [i]) as PlayerHelicopter;
				if (mini == null)
					return;

				mini.OwnerID = drivers [i].userID;
				mini.startHealth = 750f;
				mini.Spawn ();

				if (mini == null || mini.IsDestroyed)
				{
					return;
				}

				BasePlayer owner = drivers [i];

				if (owner == null || !owner.CanInteract ())
				{
					return;
				}
				foreach (var mountPoint in mini.mountPoints)
				{
					if (mountPoint.isDriver)
					{
						if (owner != null)
						{
							mountPoint.mountable.MountPlayer (drivers [i]);

						}
					}
					else if (passengers.Count > 0)
					{
						mountPoint.mountable.MountPlayer (passengers [i]);
					}
				}
				ModifyFuel (mini);
				mini.engineController.FinishStartingEngine ();
				match.minicopters.Add (mini);
				owner.SendEntityUpdate ();
			}
			if (match.crateKek == null)
			{
				Match newMatch = new Match
				{
					a = match.a,
					b = match.b,
					deathsTeamA = match.deathsTeamA,
					deathsTeamB = match.deathsTeamB,
					crateKek = StartTimedCrate (match),
					teamsCombined = match.teamsCombined,
					ended = match.ended,
					ID = match.ID,
					minicopters = match.minicopters,
					onRound = match.onRound,
					PenalizePlayers = match.PenalizePlayers,
					rounds = match.rounds,
					spectators = match.spectators,
					started = match.started,
					timeLeft = match.timeLeft,
					winner = match.winner,
					increaseRoundFlag = match.increaseRoundFlag
				};
				match = newMatch;

			}
		}
		private void ModifyFuel (BaseEntity entity)
		{
			StorageContainer container = null;
			BaseVehicle baseVehicle = entity as BaseVehicle;
			if (baseVehicle != null)
			{
				container = baseVehicle.GetFuelSystem ()?.fuelStorageInstance.Get (true);
			}
			HotAirBalloon baseBalloon = entity as HotAirBalloon;
			if (baseBalloon != null)
			{
				EntityFuelSystem fuelSystem = baseBalloon.fuelSystem;
				container = fuelSystem?.fuelStorageInstance.Get (true);
			}

			if (container == null)
			{
				return;
			}

			var item = container.inventory.GetSlot (0);
			if (item == null)
			{
				item = ItemManager.CreateByItemID (-946369541, 35); // The amount of lowgrade to add to the vehicle
				if (item == null)
				{
					return;
				}
				item.MoveToContainer (container.inventory);

				container.dropsLoot = false;
				container.inventory.SetFlag (ItemContainer.Flag.IsLocked, true);
				container.SetFlag (BaseEntity.Flags.Locked, true);
			}
		}
		private void GiveElo (Team winner, Team loser)
		{
			foreach (BasePlayer winTeammate in winner.teamMembers)
			{
				//Award(teammate);
			}
			foreach (BasePlayer loseTeammate in loser.teamMembers)
			{
				//Penalize(teammate, false);
			}
		}

		
		private void OnPlayerDisconnected (BasePlayer player, string reason)
		{
			return;
			SwapLeadership(player);
			//Kick(player);
		}
		/*
		*/
		private void SwapLeadership(BasePlayer player){
			// swap leadership if player is teamleader
			if (player.Team != null && player == player.Team.GetLeader () && player.Team.members.Count > 1)
			{
				int rand = Random.Range (0, player.Team.members.Count);
				ulong leaderID = player.Team.members [rand];
				if (leaderID == player.userID)
				{
					SwapLeadership (player);
				}
				BasePlayer leader = BasePlayer.FindByID (leaderID);
				player.Team.SetTeamLeader (leader.userID);
				leader.Team.RemovePlayer (player.userID);
			}
			else if (player.Team != null && player.Team.members.Count > 1 && player != player.Team.GetLeader ())
			{
				BasePlayer leader = player.Team.GetLeader ();
				leader.Team.RemovePlayer (player.userID);
			}
			else if (player.Team.members.Count == 1)
			{
				RelationshipManager.PlayerTeam playerOriginalTeam = player.Team;
				playerOriginalTeam.RemovePlayer (player.userID);
				playerOriginalTeam.Disband ();
				RelationshipManager.PlayerTeam PlayerTeam = RelationshipManager.ServerInstance.CreateTeam ();
				PlayerTeam.teamLeader = player.userID;
				PlayerTeam.AddPlayer (player);
			}
		}
		/*
		private void Kick(BasePlayer player){
			
			else if (player == player.Team.GetLeader() && player.Team.members.count > 1){
				SwapLeadership(player);
			}
		}
		*/
		private object OnEntityTakeDamage (BaseCombatEntity entity, HitInfo hitInfo)
        {
			var ent = entity as BaseVehicle;
			if (ent != null) //null
			{
				return null;
			}
            var victim = entity as BasePlayer;
            var attacker = hitInfo.Initiator as BasePlayer;

			float prevDamage = 0;

			if (attacker != null)
			{
				Match match = GetMatch (attacker);
				if (match.ID == 0)
					return null;

				if (match.Damages.ContainsKey (attacker))
				{
					
					foreach (KeyValuePair<BasePlayer, float> newKVP in match.Damages)
					{
						if (newKVP.Key == attacker)
						{
							prevDamage = newKVP.Value;
						}
					}
					match.Damages.Remove (attacker);
					match.Damages.Add (attacker, prevDamage + (100 - victim.health));
				}
				else
				{
					match.Damages.Add (attacker, 100 - victim.health);
				}
				return null;
			}
			return null;
        }

		private void OnEntityDeath (BaseEntity entity, HitInfo hitInfo)
		{
			if (entity == null)
				return;

			var victim = entity as BasePlayer;

			if (victim == null)
			{
				return;
			}
			if (IsInMatch (victim))
			{
				Match match = GetMatch (victim);
				if (match.ID == 0)
					return;
				if (match.spectators.Contains (victim))
					return;
				if (match.a.teamMembers.Contains (victim) && !match.deathsTeamA.Contains (victim))
				{
					match.deathsTeamA.Add (victim);
				}
				else if (!match.deathsTeamB.Contains (victim))
				{
					match.deathsTeamB.Add (victim);
				}
				victim.inventory.Strip ();
				match.spectators.Add (victim);
				//if (match.deathsTeamA.Count == match )
				SendSpectate (victim);
			}
			else
			{
				SendHome (victim);
			}
		}
		private void End (Match match)
		{
			IncreaseMatchesPlayed (match);
			int dec = Random.Range (1, 10);
			int tens = Random.Range (17, 32);
			float reward = (85.0f / 100.0f) * ((float) tens + ((float) dec / 10.0f));
			int newReward = (int) reward;
			if (match.a.roundsWon > match.b.roundsWon)
			{
				
				match.winner = match.a.identifier.displayName.ToString ();
				SendMessage (match.a, match.winner + " wins. " + match.a.roundsWon + "-" + match.b.roundsWon + ".");
				SendMessage (match.b, match.winner + " wins. " + match.a.roundsWon + "-" + match.b.roundsWon + ".");
				AwardScrap (match.a, 25);
				AwardElo (match.a, newReward);
				AwardElo (match.b, newReward * -1);
			}
			else
			{
				match.winner = match.b.identifier.displayName.ToString ();
				SendMessage (match.a, match.winner + " wins. " + match.b.roundsWon + "-" + match.a.roundsWon + ".");
				SendMessage (match.b, match.winner + " wins. " + match.b.roundsWon + "-" + match.a.roundsWon + ".");
				AwardScrap (match.b, 25);
				AwardElo (match.b, newReward);
				AwardElo (match.a, newReward * -1);
			}

			if (match.a.roundsWon == 3 && match.b.roundsWon == 0)
			{
				Server.Broadcast (match.b.identifier.displayName.ToString () + " got rocked, 3-0.");
			}
			else if (match.b.roundsWon == 3 && match.a.roundsWon == 0)
			{
				Server.Broadcast (match.a.identifier.displayName.ToString () + " got rocked, 3-0.");
			}
			foreach (BasePlayer player in match.teamsCombined)
			{
				if (player != null && player.IsConnected && player.IsValid ())
					SendHome (player);
			}

			foreach (PlayerHelicopter mini in match.minicopters)
			{
				if (mini != null || !mini.IsDestroyed)
				{
					mini.Kill ();
				}
			}

			match.minicopters.Clear ();
			match.spectators.Clear ();
			match.deathsTeamA.Clear ();
			match.deathsTeamB.Clear ();
			match.ended = true;
			match.started = false;
			match.ID = 0;
			match.onRound = 0;
			match.rounds = 0;
			match.winner = null;
			match.timeLeft = 0;
			if (match.crateKek != null)
			{
				match.crateKek.Kill ();
				match.crateKek = null;
			}

			/*
			if (match.PenalizePlayers.Count > 0)
			{
				foreach (BasePlayer rat in match.PenalizePlayers)
				{
					Penalize (rat, true, );
				}
			}
			*/
			match.a.teamMembers.Clear ();
			match.b.teamMembers.Clear ();
			if (Teams.ContainsKey (match.a))
			{
				Teams.Remove (match.a);
			}
			if (Teams.ContainsKey (match.b))
			{
				Teams.Remove (match.b);
			}
			Matches.Clear ();
		}

		private void ForceEnd (Match match)
		{
			foreach (BasePlayer player in match.teamsCombined)
			{
				if (player != null && player.IsConnected && player.IsValid())
					SendHome (player);
			}

			foreach (PlayerHelicopter mini in match.minicopters)
			{
				if (mini != null || !mini.IsDestroyed)
				{
					mini.Kill ();
				}
			}

			match.minicopters.Clear ();
			match.spectators.Clear ();
			match.deathsTeamA.Clear ();
			match.deathsTeamB.Clear ();
			match.ended = true;
			match.started = false;
			match.ID = 0;
			match.onRound = 0;
			match.rounds = 0;
			match.winner = null;
			match.timeLeft = 0;

			
			if (match.crateKek != null)
			{
				match.crateKek.Kill ();
				match.crateKek = null;
			}
			/*
			if (match.PenalizePlayers.Count > 0)
			{
				foreach (BasePlayer rat in match.PenalizePlayers)
				{
					Penalize (rat, true, );
				}
			}
			*/
			match.a.teamMembers.Clear ();
			match.b.teamMembers.Clear ();
			if (Teams.ContainsKey (match.a))
			{
				Teams.Remove (match.a);
			}
			if (Teams.ContainsKey (match.b))
			{
				Teams.Remove (match.b);
			}
		}

		private void IncreaseMatchesPlayed (Match match)
		{
			foreach (BasePlayer teamAPlayer in match.a.teamMembers)
			{
				SetMatchesPlayed (teamAPlayer.userID, GetMatchesPlayed (teamAPlayer.userID) + 1);
			}
			foreach (BasePlayer teamBPlayer in match.b.teamMembers)
			{
				SetMatchesPlayed (teamBPlayer.userID, GetMatchesPlayed (teamBPlayer.userID) + 1);
			}
		}

		private void Unload ()
		{
			if (Matches.Count > 0)
			{
				foreach (Match match in Matches)
				{
					match.unloading = true;
					ForceEnd (match);
					foreach (BasePlayer player in match.teamsCombined)
					{
						SwapLeadership (player);
					}
				}
			}

			Matches.Clear ();
			SaveData ();
		}

		private void SendHome (BasePlayer player)
		{
			Vector3 spawn;
			spawn.x = -140.41f;
			spawn.y = 20.21f;
			spawn.z = -321.54f;
			player.inventory.Strip ();
			player.inventory.containerWear.SetLocked (false);
			player.Respawn ();
			Server.Broadcast ("Respawning in sendhome");
			Teleport (player, spawn);
			DeMetabolize (player);
		}

		private void SendSpectate (BasePlayer player)
		{
			Vector3 deathLobby;
			deathLobby.x = -119.57f;
			deathLobby.y = 15.11f;
			deathLobby.z = 103.78f;

			var match = GetMatch (player);

			if (match.a.teamMembers.Contains (player) && match.a.teamMembers.Count == 1)
			{
				player.Respawn ();
				return;
			}
			else if (match.b.teamMembers.Count == 1)
			{
				player.Respawn ();
				return;
			}
			/*
			deathLobby.x = -417.66f;
			deathLobby.y = 20.08f;
			deathLobby.z = -226.21f;
			*/
			player.RespawnAt (deathLobby, player.ServerRotation);
			//Teleport (player, deathLobby, true);
		}
		private bool IsInMatch (BasePlayer player)
		{
			foreach (Match match in Matches)
			{
				foreach (BasePlayer entry in match.teamsCombined)
				{
					if (player != null && entry == player && match.ID != 0)
					{
						return true;
					}
				}
			}
			return false;
		}

		private void Penalize (BasePlayer player, bool leaver, int multiplier)
		{
			return;
			//DisconnectedPlayersAlert.Add(player, eloLost);
			if (multiplier != 0)
			{

			}
			else
			{

			}
		}

		private Match GetMatch (BasePlayer player)
		{
			Match matchNotFound = new Match ();
			foreach (Match match in Matches)
			{
				foreach (BasePlayer entry in match.teamsCombined)
				{
					if (entry == player)
					{
						return match;
					}
				}
				MatchSet (matchNotFound, match);
			}
			matchNotFound.ID = 0;
			return matchNotFound;
		}

		private void MatchSet (Match receiver, Match setter)
		{
			receiver.a = setter.a;
			receiver.b = setter.b;
			receiver.started = setter.started;
			receiver.onRound = setter.onRound;
			receiver.rounds = setter.rounds;
			receiver.ended = setter.ended;
			receiver.ID = setter.ID; // Unique key based on the leaders of both teams' game IDs
			receiver.deathsTeamA = setter.deathsTeamA;
			receiver.deathsTeamB = setter.deathsTeamB;
			receiver.spectators = setter.spectators;
			receiver.teamsCombined = setter.teamsCombined;
		}

		object OnTeamKick (RelationshipManager.PlayerTeam playerTeam, BasePlayer player, ulong target)
		{
			if (IsInMatch (player))
			{
				return false;
			}
			else
			{
				return null;
			}
		}

		object OnTeamLeave (RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
		{
			if (IsInMatch (player))
			{
				return false;
			}
			else
			{
				return null;
			}
		}
		private void OnPlayerConnected (BasePlayer player)
		{
			if (!player.IsValid ())
			{
				return;
			}
			SendHome (player);

			if (player.Team == null)
			{
				RelationshipManager.PlayerTeam PlayerTeam = RelationshipManager.ServerInstance.CreateTeam ();
				PlayerTeam.teamLeader = player.userID;
				PlayerTeam.AddPlayer (player);
			}

			/*
			foreach (KeyValuePair<BasePlayer, float> disconnect in DisconnectedPlayersAlert){
				if (player == disconnect.Key)
					SendReply(disconnect.Key.displayName.ToString(), "Nice disconnect during the last match you played; you were penalized an extra " + disconnect.Value + " elo for leaving.");
			}
			*/
		}

		private void AwardScrap (Team team, int amount)
		{
			foreach (BasePlayer awarded in team.teamMembers)
			{
				ScraponomicsLite.Call ("SetBalance", awarded.userID, (int) ScraponomicsLite.Call ("GetBalance", awarded.userID) + amount);
				string chatOutput = _config.Format
						.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
						.Replace ("{Message}", $"{"You were awarded " + amount + " scrap to your ATM for winning the match!"}");
				SendReply (awarded, chatOutput);
			}
		}

		private void AwardElo (Team team, float amount)
		{
			foreach (BasePlayer awarded in team.teamMembers)
			{
				SetElo (awarded.userID, GetElo(awarded.userID) + amount);
				string chatOutput = _config.Format
					.Replace ("{Title}", $"<color={_config.TitleColor}>{_config.Title}</color>")
					.Replace ("{Message}", $"{"You were awarded " + amount +" <color=#ed2323><size=10>ELO</size></color> for winning the match!"}");
				SendReply (awarded, chatOutput);
			}
		}

		private void SubscribeHooks (bool flag)
		{
			if (flag)
			{
				Subscribe (nameof (OnPlayerConnected));
				Subscribe (nameof (OnPlayerDisconnected));
				Subscribe (nameof (OnEntityDeath));
				Subscribe (nameof (OnTeamKick));
				Subscribe (nameof (OnTeamLeave));
				//Subscribe (nameof (OnCrateHackEnd));
			}
			else
			{
				Unsubscribe (nameof (OnPlayerConnected));
				Unsubscribe (nameof (OnPlayerDisconnected));
				Unsubscribe (nameof (OnEntityDeath));
				Unsubscribe (nameof (OnTeamKick));
				Unsubscribe (nameof (OnTeamLeave));
				//Unsubscribe (nameof (OnCrateHackEnd));
			}
		}

		private void CreateTip (string text, BasePlayer player, float length = 30f)
		{
			if (player == null)
				return;
			player.SendConsoleCommand ("gametip.hidegametip");
			player.SendConsoleCommand ("gametip.showgametip", text);
			timer.Once (length, () => player?.SendConsoleCommand ("gametip.hidegametip"));
		}

		private void OnServerSave ()
		{
			SaveData ();
		}

		private void SetElo (ulong userId, float newElo)
		{
			if (!playerElo.ContainsKey (userId) && !TryInitPlayer (userId))
				return;

			playerElo [userId].elo = newElo;
		}

		private float GetElo (ulong userId)
		{
			if (!playerElo.ContainsKey (userId) && !TryInitPlayer (userId))
				return 0;
			return playerElo [userId].elo;
		}

		private void SetMatchesPlayed (ulong userId, int newMatchesPlayed)
		{
			if (!playerElo.ContainsKey (userId) && !TryInitPlayer (userId))
				return;

			playerElo [userId].matchesPlayed = newMatchesPlayed;
		}

		private int GetMatchesPlayed (ulong userId)
		{
			if (!playerElo.ContainsKey (userId) && !TryInitPlayer (userId))
				return 0;
			return playerElo [userId].matchesPlayed;
		}

		private void Init ()
		{
			ReadData ();
			LoadConfig ();
			base.LoadConfig ();
			_config = Config.ReadObject<Configuration> ();
			SaveConfig ();
		}

		protected override void SaveConfig () => Config.WriteObject (_config);

		protected override void LoadDefaultConfig () => _config = new Configuration ();

		private void InitPlayerElo (BasePlayer player)
		{
			var playerbalances = new PlayerElo
			{
				elo = 1000
			};
			playerElo.Add (player.userID, playerbalances);
		}

		private bool TryInitPlayer (ulong userId)
		{
			BasePlayer player = BasePlayer.FindByID (userId);
			if (player == null)
				return false;
			InitPlayerElo (player);
			return true;
		}

		private void SaveData () =>
			Interface.Oxide.DataFileSystem.WriteObject ("PlayersData" , playerElo);

		private void ReadData () =>
			playerElo = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerElo>> ("PlayersData");

		#endregion
	}
}
