using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
	[Info ("OilRigGamemode", "Blood", "0.0.1")]
	[Description ("A gamemode inviting users to PvP on Oil Rig")]
	public class OilRigGamemode : RustPlugin
	{
		private const string minicopterAssetPrefab = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
		private const string crateAssetPrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";

		struct Team
		{
			public List<BasePlayer> TeamMembers;
			public List<BasePlayer> ConnectedMembers;
			public bool Color;
			public string GamemodeQueuedFor;
			public BasePlayer Identifier;
			public bool InGame;         
			public ulong MatchID;
			public int RoundsWon;
			public int Points;
			public int CrateHackWins;
			public bool CanForfeit;
		};
		struct Match
		{
			public Team a;
			public Team b;
			public int OnRound;
			public int Rounds;
			public bool Started;
			public bool Ended;
			public List<BasePlayer> TeamsCombined;
			public List<BasePlayer> DeathsTeamA;
			public List<BasePlayer> DeathsTeamB;
			public List<BasePlayer> Spectators;
			public List<BasePlayer> PenalizePlayers;
			public List<PlayerHelicopter> Minicopters;
			public ulong ID;
			public string Winner;
			public float TimeLeft;
			public HackableLockedCrate LockedCrate;
			public bool IncreaseRoundFlag;
			public bool RoundOver;
		};

		List<Match> Matches = new List<Match>();                  
		Dictionary<Team, int> Teams = new Dictionary<Team, int>();
		Dictionary<BasePlayer, float> DisconnectedPlayersAlert = new Dictionary<BasePlayer, float>();

		#region ChatCommands
		[ChatCommand ("forfeit")]
		private void ForfeitCMD(BasePlayer caller){
			if (IsInMatch(caller)){
				Match match = GetMatch(caller);

				if (match.a.CanForfeit && match.a.TeamMembers.Contains(caller)){
					End(match);
					match.a.CanForfeit = false;
				}
				else if (match.b.CanForfeit){
					End(match);
					match.a.CanForfeit = false;
				}
				else{
					SendReply(caller, "You don't have permission to do this.");
				}
			}
			else{
				SendReply(caller, "You don't have permission to do this.");
			}
		}


		[ChatCommand ("qleave")]
		private void LeaveQueue(BasePlayer caller)
		{
			KeyValuePair<Team, int> removeTeam = new KeyValuePair<Team, int> ();
			bool FoundTeamInQueue = false;
			if (caller == null)
				return;

			if (caller == caller.Team.GetLeader())
			{
				foreach (KeyValuePair<Team, int> Entry in Teams)
				{
					if (Entry.Key.Identifier == caller)
					{
						removeTeam = Entry;
						FoundTeamInQueue = true;
						break;
					}
				}
				if (FoundTeamInQueue)
				{
					Teams.Remove (removeTeam.Key);
					SendReply (caller, "Successfully removed your team from the queue.");
				}
				else
				{
					SendReply (caller, "Your team is not in the queue, try queuing first.");
				}
			}
			else
			{
				SendReply (caller, "Only the Leader of your team can cancel a queue.");
			}
		}

		[ChatCommand("rig")]
		private void CmdRig (BasePlayer initiator)
		{
			if (initiator == null) return;

			if (initiator.Team == null || initiator.currentTeam < 1)
			{
				initiator.ChatMessage ("Not a large enough group for this event. Please ensure that you are on a team.");
				return;
			}
			foreach (KeyValuePair<Team, int> check in Teams)
			{
				if (check.Key.Identifier == initiator)
				{
					SendReply (initiator, "Your team is already in the Queue for " + check.Key.GamemodeQueuedFor + ".");
					return;
				}
			}
			if (initiator != initiator.Team.GetLeader())
			{
				SendReply (initiator, "Only the team leader is allowed to initiate gamemodes.");
				return;
			}

			if (IsInMatch (initiator))
			{
				SendReply(initiator, "You are already in a match.");
				return;
			}
			// Check if the person queuing is already in the queue

			else
			{
				int teamcount = 0;
				Team NewTeam = new Team
				{
					TeamMembers = new List<BasePlayer> (),
					ConnectedMembers = new List<BasePlayer>(),
					Identifier = initiator,
					GamemodeQueuedFor = "",
					InGame = false
				};
				foreach (ulong teamMemberSteamID64 in initiator.Team.members)
				{
					BasePlayer Teammate = BasePlayer.FindByID (teamMemberSteamID64);
					if (Teammate != null && Teammate.IsConnected)
					{
						SendReply (initiator, "Member connected: " + Teammate.displayName.ToString ());
						NewTeam.TeamMembers.Add (Teammate);
						NewTeam.ConnectedMembers.Add (Teammate);
						teamcount++;
					}
					else if (Teammate != null && !Teammate.IsConnected)
					{
						initiator.Team.RemovePlayer (Teammate.userID);
					}
				}
				switch (teamcount)
				{
					case 1:
						NewTeam.GamemodeQueuedFor = "Solos.";
						break;

					case 2:
						NewTeam.GamemodeQueuedFor = "Duos.";
						break;

					case 3:
						NewTeam.GamemodeQueuedFor = "Trios.";
						break;

					case 4:
						NewTeam.GamemodeQueuedFor = "Quads.";
						break;

					default:
						SendReply (initiator, "Team size invalid for this gamemode (Small Oil Rig). Err: 1 < Team size < 5.");
						return;
				}
				Teams.Add (NewTeam, teamcount);
				SendMessage (NewTeam, "Now queuing for " + NewTeam.GamemodeQueuedFor);

				List<Team> TeamsToRemove = new List<Team> ();
				Timer displayMessageTimer = null;
				Timer newTimer = null;

				newTimer = timer.Every (3f, () =>
				{
					if (Teams.Count > 1)
					{
						KeyValuePair<Team, int> team = Teams.First ();
						KeyValuePair<Team, int> Entry = new KeyValuePair<Team, int> ();
						foreach (KeyValuePair<Team, int> check in Teams)
						{
							if (check.Key.TeamMembers != team.Key.TeamMembers && team.Key.TeamMembers.Count == check.Key.TeamMembers.Count)
							{
								TeamsToRemove.Add (team.Key);
								TeamsToRemove.Add (check.Key);
								if (newTimer != null)
								{
									newTimer.Destroy ();
									newTimer = null;
								}
								Entry = check;
								break;
							}
						}
						StartGame (team, Entry);
						foreach (Team remove in TeamsToRemove)
						{
							Teams.Remove (remove);
						}
						TeamsToRemove.Clear ();
					}
				});
				displayMessageTimer = timer.Every (20f, () =>
				{
					if (Teams.ContainsKey (NewTeam))
					{
						SendReply (initiator, "You are in the Queue for " + NewTeam.GamemodeQueuedFor); // + " at an elo of " + getElo(initiator));
					}
					else
					{
						displayMessageTimer.Destroy ();
						displayMessageTimer = null;
					}
				});
			}
		}
		#endregion

		private void StartGame (KeyValuePair<Team, int> teamA, KeyValuePair<Team, int> teamB)
		{
			bool selectedTeamColor = Random.Range (0, 2) == 1;
			bool selectedEntryColor = !selectedTeamColor;

			Team teamANew = new Team {
				Color =				selectedTeamColor,
				GamemodeQueuedFor = teamA.Key.GamemodeQueuedFor,
				MatchID =			teamA.Key.MatchID,
				Identifier =		teamA.Key.Identifier,
				InGame =			teamA.Key.InGame,
				TeamMembers =		teamA.Key.TeamMembers,
				ConnectedMembers =	teamA.Key.ConnectedMembers,
				RoundsWon =			0
			};

			Team teamBNew = new Team
			{
				Color =				selectedEntryColor,
				GamemodeQueuedFor = teamB.Key.GamemodeQueuedFor,
				MatchID =			teamB.Key.MatchID,
				Identifier =		teamB.Key.Identifier,
				InGame =			teamB.Key.InGame,
				TeamMembers =		teamB.Key.TeamMembers,
				ConnectedMembers =	teamB.Key.ConnectedMembers,
				RoundsWon =			0
			};

			KeyValuePair<Team, int> teamAKVP = new KeyValuePair<Team, int> (teamANew, teamANew.TeamMembers.Count);
			KeyValuePair<Team, int> teamBKVP = new KeyValuePair<Team, int> (teamBNew, teamBNew.TeamMembers.Count);

			teamANew.InGame = true;
			teamBNew.InGame = true;

			Match newMatch = new Match
			{
				a = teamANew,
				b = teamBNew,
				Started = false,
				OnRound = 1,
				Rounds = 3,
				Ended = false,
				ID = teamANew.Identifier.userID + teamBNew.Identifier.userID, // Unique key based on the leaders of both teams' game IDs
				DeathsTeamA = new List<BasePlayer> (),
				DeathsTeamB = new List<BasePlayer> (),
				Spectators = new List<BasePlayer> (),
				TeamsCombined = new List<BasePlayer> (),
				PenalizePlayers = new List<BasePlayer> (),
				Minicopters = new List<PlayerHelicopter> (),
			};

			if (!newMatch.Started && newMatch.b.Color)
			{
				SendMessage (newMatch.b, "You are on the <color=#316bf5>BLUE</color> team.");
				SendMessage (newMatch.b, "You must wait 10 seconds before <color=#ed2323>DEFENDING</color> the OilRig from your opponent.");
				newMatch.Started = true;
			}
			else if (!newMatch.Started && !newMatch.b.Color)
			{
				SendMessage (newMatch.b, "You are on the <color=#ed2323>RED</color> team.");
				SendMessage (newMatch.b, "Get to the Oil Rig and successfully <color=#ed2323>ELIMINATE</Color> all enemies before the 5 minute timer runs out.");
				newMatch.Started = true;
			}

			if (!newMatch.Started && !newMatch.a.Color)
			{
				SendMessage (newMatch.a, "You are on the <color=#ed2323>RED</color> team.");
				SendMessage (newMatch.a, "Get to the Oil Rig and successfully <color=#ed2323>ELIMINATE</color> all enemies before the 5 minute timer runs out.");
				newMatch.Started = true;
			}
			else if (!newMatch.Started && newMatch.a.Color)
			{
				SendMessage (newMatch.a, "You are on the <color=#316bf5>BLUE</color> team.");
				SendMessage (newMatch.a, "You must wait 10 seconds before <color=#ed2323>DEFENDING</color> the OilRig from your opponent.");
				newMatch.Started = true;
			}

			var objec = newMatch;
			objec.LockedCrate = StartTimedCrate (newMatch);
			newMatch = objec;

			newMatch.TeamsCombined = CombineTeams (newMatch.a, newMatch.b);

			SendToGame (teamAKVP, teamANew.Color, newMatch);
			SendMessage (teamAKVP.Key, "You are facing team, " + teamBKVP.Key.Identifier.displayName + ".");

			SendToGame (teamBKVP, teamBNew.Color, newMatch);
			SendMessage (teamBKVP.Key, "You are facing team, " + teamAKVP.Key.Identifier.displayName + ".");

			Matches.Add (newMatch);
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
				GamemodeQueuedFor = match.a.GamemodeQueuedFor,
				MatchID = match.a.MatchID,
				Identifier = match.a.Identifier,
				InGame = match.a.InGame,
				TeamMembers = match.a.TeamMembers,
				ConnectedMembers = match.a.ConnectedMembers,
				RoundsWon = match.a.RoundsWon
			};

			if (match.a.color)
			{
				Server.Broadcast ("Increasing roundwon of team blue " + teamANew.RoundsWon);
				var objec = teamANew;
				objec.RoundsWon++;
				teamANew = objec;
				Server.Broadcast ("after increasing roundwon of team blue " + teamANew.RoundsWon);
			}

			Team teamBNew = new Team
			{
				color = match.b.color,
				GamemodeQueuedFor = match.b.GamemodeQueuedFor,
				MatchID = match.b.MatchID,
				Identifier = match.b.Identifier,
				InGame = match.b.InGame,
				TeamMembers = match.b.TeamMembers,
				ConnectedMembers = match.b.ConnectedMembers,
				RoundsWon = match.b.RoundsWon
			};

			if (match.b.color)
			{
				Server.Broadcast ("Increasing roundwon of team blue " + teamBNew.RoundsWon);
				var objec = teamBNew;
				objec.RoundsWon++;
				teamBNew = objec;
				Server.Broadcast ("after increasing roundwon of team blue " + teamBNew.RoundsWon);
			}

			//KeyValuePair<Team, int> teamAKVP = new KeyValuePair<Team, int> (teamANew, teamANew.TeamMembers.Count);
			//KeyValuePair<Team, int> teamBKVP = new KeyValuePair<Team, int> (teamBNew, teamBNew.TeamMembers.Count);

			Match match2 = new Match {
				a = teamANew,
				b = teamBNew,
				DeathsTeamA = match.DeathsTeamA,
				DeathsTeamB = match.DeathsTeamB,
				crateKek = match.crateKek,
				TeamsCombined = match.TeamsCombined,
				Ended = match.Ended,
				ID = match.ID,
				Minicopters = match.Minicopters,
				OnRound = match.OnRound,
				PenalizePlayers = match.PenalizePlayers,
				Rounds = match.Rounds,
				Spectators = match.Spectators,
				Started = match.Started,
				TimeLeft = match.TimeLeft,
				Winner = match.Winner,
				IncreaseRoundFlag = true
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

			Server.Broadcast ("Checking match in roundincrease with flag of " + match2.IncreaseRoundFlag);
			CheckGame (match2);
		}
		*/
		private void SendToGame (KeyValuePair<Team, int> team, bool Color, Match match)
		{
			match.RoundOver = false;

			Vector3 [] rigSpawns = new Vector3[4];
			Vector3 outsideSpawn;

			rigSpawns[0].x = -323.398f;
			rigSpawns[0].y = 22.4263f;
			rigSpawns[0].z = -364.819f;

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
			int i = 0;
			foreach (BasePlayer teamMember in team.Key.TeamMembers)
			{
				if (i % 4 == 0){
					i = 0;
				}
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
				if (Color)
				{
					Teleport (teamMember, rigSpawns[i]);
					timer.Repeat (.125f, 80, () =>
					{
						bool move = (teamMember.transform.position.x >= rigSpawns[i].x + 2.0f || 
									 teamMember.transform.position.z >= rigSpawns[i].z + 2.0f) || 

									(teamMember.transform.position.x <= rigSpawns [i].x - 2.0f || 
									 teamMember.transform.position.z <= rigSpawns [i].z - 2.0f) || 
									
									(teamMember.transform.position.x >= rigSpawns [i].x + 2.0f || 
									 teamMember.transform.position.z <= rigSpawns [i].z - 2.0f) || 
									
									(teamMember.transform.position.x <= rigSpawns [i].x - 2.0f || 
									 teamMember.transform.position.z >= rigSpawns [i].z + 2.0f);

						if (teamMember != null && move)
							teamMember.MovePosition (rigSpawns[i]);
					});
				}
				else
				{
					Vector3 newSpawn;
					newSpawn.x = outsideSpawn.x;
					newSpawn.y = outsideSpawn.y + 25.0f;
					newSpawn.z = outsideSpawn.z;

					Teleport (teamMember, newSpawn);
				}

				GiveKit (teamMember, Color);
				Metabolize (teamMember);
				i++;
			}
			if (Color == false){
				OnEntitySpawned (team.Key, match);
			}
		}

		private HackableLockedCrate StartTimedCrate (Match match){
			Vector3 cratePos;
			cratePos.x = -322.63f;
			cratePos.y = 27.18f;
			cratePos.z = -342.04f;

			const float REQUIREDHACKSECONDS = 10f;
			HackableLockedCrate LockedCrate;
			LockedCrate = (HackableLockedCrate) GameManager.server.CreateEntity (crateAssetPrefab, cratePos, match.a.Identifier.ServerRotation);
			
			if (LockedCrate != null)
			{
				LockedCrate.SpawnAsMapEntity ();
				LockedCrate.StartHacking ();
				LockedCrate.shouldDecay = false;
				LockedCrate.enableSaving = false;
				LockedCrate.hackSeconds = HackableLockedCrate.requiredHackSeconds - REQUIREDHACKSECONDS;
				return LockedCrate;
			}
			return LockedCrate;
		}
		private void CheckGame(Match match)
		{
			Timer myTimer = null;
			myTimer = timer.Every (1f, () =>
			{
					if (match.ID == 0)
					{
						Server.Broadcast ("Something broke in this bitch fr, match ID is 0 ");
					}
				/*
				foreach (BasePlayer Teammate in match.a.TeamMembers)
				{
					if (Teammate != null && Teammate.IsConnected && !match.a.ConnectedMembers.Contains(Teammate)){
						match.a.ConnectedMembers.Add(Teammate);
					}	
					else if (Teammate != null && !Teammate.IsConnected){
						Kick(Teammate);
					}
				}
				foreach (BasePlayer Teammate2 in match.b.TeamMembers)
				{
					if (Teammate2 != null && Teammate2.IsConnected && !match.b.ConnectedMembers.Contains(Teammate2)){
						match.b.ConnectedMembers.Add (Teammate2);
					}
					else if (Teammate2 != null && !Teammate2.IsConnected){
						Kick(Teammate2);
					}	
				}
				*/
				//match.a.Identifier.Team.
				if (match.IncreaseRoundFlag && match.Rounds == match.OnRound)
				{
					match.RoundOver = true;
					SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
					SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
					End (match);
					match.IncreaseRoundFlag = false;
					if (myTimer != null)
					{
						myTimer.Destroy ();
						myTimer = null;
					}
				}
				else if (match.IncreaseRoundFlag && match.OnRound < match.Rounds)
				{
					if (match.b.Color) {
						Team teamB = new Team
						{
							Color = match.b.Color,
							MatchID = match.b.MatchID,
							GamemodeQueuedFor = match.b.GamemodeQueuedFor,
							Identifier = match.b.Identifier,
							InGame = match.b.InGame,
							TeamMembers = match.b.TeamMembers,
							ConnectedMembers = match.b.ConnectedMembers,
							RoundsWon = match.b.RoundsWon + 1
						};
						SendMessage (teamB, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
						SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
						match.b = teamB;
					}
					else {
						Team teamA = new Team
						{
							Color = match.a.Color,
							MatchID = match.a.MatchID,
							GamemodeQueuedFor = match.a.GamemodeQueuedFor,
							Identifier = match.a.Identifier,
							InGame = match.a.InGame,
							TeamMembers = match.a.TeamMembers,
							ConnectedMembers = match.a.ConnectedMembers,
							RoundsWon = match.a.RoundsWon + 1
						};
						SendMessage (teamA, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
						SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round by expiring the crate time.");
						match.b = teamA;
					}
					match.OnRound++;
					match.IncreaseRoundFlag = false;
					match.Ended = false;
					Reset (ref match);
				}
				else
				{
					//match.TimeLeft++;
					if (match.Rounds == match.OnRound && (match.DeathsTeamA.Count >= match.a.TeamMembers.Count || match.DeathsTeamB.Count >= match.b.TeamMembers.Count))
					{
						End (match);
						if (myTimer != null)
						{
							myTimer.Destroy ();
							myTimer = null;
						}
					}
					else if (match.DeathsTeamB.Count >= match.b.TeamMembers.Count || match.DeathsTeamA.Count >= match.a.TeamMembers.Count)
					{
						if (match.DeathsTeamA.Count >= match.a.TeamMembers.Count)
						{
							Team teamB = new Team
							{
								Color = match.b.Color,
								MatchID = match.b.MatchID,
								GamemodeQueuedFor = match.b.GamemodeQueuedFor,
								Identifier = match.b.Identifier,
								InGame = match.b.InGame,
								TeamMembers = match.b.TeamMembers,
								ConnectedMembers = match.b.ConnectedMembers,
								RoundsWon = match.b.RoundsWon + 1
							};

							if (match.b.Color)
							{
								SendMessage (teamB, "<color=#316bf5>BLUE</color> won the round.");
								SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round.");
							}
							else
							{
								SendMessage (teamB, "<color=#ed2323>RED</color> won the round.");
								SendMessage (match.a, "<color=#ed2323>RED</color> won the round.");
							}
							match.RoundOver = true;
							match.b = teamB;
						}
						else
						{
							Team teamA = new Team
							{
								Color = match.a.Color,
								MatchID = match.a.MatchID,
								GamemodeQueuedFor = match.a.GamemodeQueuedFor,
								Identifier = match.a.Identifier,
								InGame = match.a.InGame,
								TeamMembers = match.a.TeamMembers,
								ConnectedMembers = match.a.ConnectedMembers,
								RoundsWon = match.a.RoundsWon + 1
							};
							if (match.a.Color)
							{
								SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round.");
								SendMessage (teamA, "<color=#316bf5>BLUE</color> won the round.");
							}
							else
							{
								SendMessage (match.b, "<color=#ed2323>RED</color> won the round.");
								SendMessage (teamA, "<color=#ed2323>RED</color> won the round.");
							}
							match.RoundOver = true;
							match.a = teamA;
						}
						match.OnRound++;
						match.Ended = false;
						Reset (ref match);
					}
					match.Ended = false;
					//if (onlineConnectionsTeamA.Count != match.a.Identifier.Team.members.Count)
					//{
					//passengers = teamList.Except (drivers).ToList ();
					//	removePlayersTeamA = match.a.Identifier.Team.members.Except (onlineConnectionsTeamA.Values).ToList ();
					//}
					//if (onlineConnectionsTeamB.Count != match.b.Identifier.Team.members.Count)
					//{
					//passengers = teamList.Except (drivers).ToList ();
					//	removePlayersTeamB = match.b.Identifier.Team.members.Except (onlineConnectionsTeamB.Values).ToList ();
					//}
				}
			});
			Timer checkCrateTimer = null;
			checkCrateTimer = timer.Repeat (10f, 3, () =>
			{
				if (!match.RoundOver && match.LockedCrate != null)
				{
					//SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round by successully expiring the crate time.");
					//SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round by successully expiring the crate time.");
					match.IncreaseRoundFlag = true;
					match.Ended = true;
					//RoundIncrease (match);
				}
				else
				{
					checkCrateTimer.Destroy ();
					checkCrateTimer = null;
				}
			});
		}

		private void Reset (ref Match match)
		{
			if (match.ID == 0)
			{
				return;
			}

			if (match.LockedCrate != null)
			{
				match.LockedCrate.Kill ();
				match.LockedCrate = null;
			}

			match.LockedCrate = StartTimedCrate (match);

			//AwardPoints (match);
			List<PlayerHelicopter> Minicopterstoremove = new List<PlayerHelicopter> ();

			if (match.Minicopters != null)
			{
				foreach (PlayerHelicopter mini in match.Minicopters)
				{
					if (mini != null && !mini.IsDestroyed)
					{
						mini.Kill ();
						if (match.Minicopters != null) Minicopterstoremove.Add (mini);
					}
				}
				foreach (PlayerHelicopter minicopter in Minicopterstoremove)
				{
					match.Minicopters.Remove (minicopter);
				}
			}
			if (!match.Ended)
			{
				Respawn (match);
			}
		}

		private void Respawn (Match match)
		{
			KeyValuePair<Team, int> teamA = new KeyValuePair<Team, int> (match.a, match.a.TeamMembers.Count);
			KeyValuePair<Team, int> teamB = new KeyValuePair<Team, int> (match.b, match.b.TeamMembers.Count);
			match.DeathsTeamA.Clear ();
			match.DeathsTeamB.Clear ();
			match.Spectators.Clear ();
			SendToGame (teamA, match.a.Color, match);
			SendToGame (teamB, match.b.Color, match);
		}


		#region Helper Functions
		private List<BasePlayer> CombineTeams (Team a, Team b)
		{
			List<BasePlayer> players = new List<BasePlayer> ();
			foreach (BasePlayer player in a.TeamMembers)
			{
				if (player != null)
					players.Add (player);
			}
			foreach (BasePlayer player2 in b.TeamMembers)
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
				weapon.SendNetworkUpdateImmediate(false); // update
				weapon.primaryMagazine.contents = weapon.primaryMagazine.capacity; // load new ammo
			}
			GiveItems (Inventory, 1, Inventory.containerWear);                                                                  // ARMOR
			if (teamColor)
			{
				Inventory.GiveItem (ItemManager.CreateByItemID (1751045826, 1, 14178), Inventory.containerWear);				// HOODIE
				Inventory.GiveItem (ItemManager.CreateByItemID (237239288, 1, 10001), Inventory.containerWear);					// PANTS
			}
			else
			{
				Inventory.GiveItem (ItemManager.CreateByItemID (1751045826, 1), Inventory.containerWear);                       // HOODIE
				Inventory.GiveItem (ItemManager.CreateByItemID (237239288, 1, 3099244148), Inventory.containerWear);			// PANTS
			}
			Inventory.GiveItem (item);                                                                                          // ASSAULT RIFLE
			Inventory.GiveItem (ItemManager.CreateByItemID (-1211166256, 128), Inventory.containerMain);						// AMMO
			Inventory.GiveItem (ItemManager.CreateByItemID (1079279582, 2), Inventory.containerBelt);                           // SYRINGE
			Inventory.GiveItem (ItemManager.CreateByItemID (1079279582, 2), Inventory.containerBelt);                           // SYRINGE
			Inventory.GiveItem (ItemManager.CreateByItemID (1079279582, 1), Inventory.containerBelt);                           // SYRINGE
			Inventory.GiveItem (ItemManager.CreateByItemID (-2072273936, 3), Inventory.containerBelt);                          // BANDAGE
		}

		private void SendMessage (Team team, string message)
		{
			foreach (BasePlayer player in team.TeamMembers)
			{
				if (player != null)
					SendReply (player, message);
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
			player.metabolism.calories.SetValue(81);
			player.metabolism.hydration.Reset();
			player.metabolism.SendChangesToClient ();
		}

		public void Teleport (BasePlayer player, Vector3 destination)
		{
			if (player == null || destination == Vector3.zero)
			{
				return;
			}

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

			if (player.IsDead ())
			{
				player.Respawn ();
			}

			Metabolize (player);

			player.Teleport (destination);

			if (player.IsConnected && (Vector3.Distance (player.transform.position, destination) > 50f))
			{
				player.SetPlayerFlag (BasePlayer.PlayerFlags.ReceivingSnapshot, true);
				player.ClientRPCPlayer (null, player, "StartLoading");
				player.UpdateNetworkGroup ();
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
			List<int> randIntList = new List<int> ();
			List<BasePlayer> drivers = new List<BasePlayer> ();
			List<BasePlayer> passengers = new List<BasePlayer> ();

			int miniCount = 0;

			if (team.TeamMembers.Count % 2 == 0)
			{
				miniCount = team.TeamMembers.Count / 2;
			}
			else
			{
				miniCount = (team.TeamMembers.Count / 2) + 1;
			}
			Vector3 [] newPositions = new Vector3 [miniCount];
			for (float i = 0; i < miniCount; i += 1)
			{
				Vector3 newPos;
				newPos.x = (team.Identifier.transform.position.x + (3.5f * i));
				newPos.y = team.Identifier.transform.position.y;
				newPos.z = team.Identifier.transform.position.z;
				newPositions [((int) i)] = newPos;
			}
			foreach (BasePlayer player in team.TeamMembers)
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
				PlayerHelicopter mini = team.Identifier.GetComponentInParent<PlayerHelicopter> ();

				mini = GameManager.server.CreateEntity (minicopterAssetPrefab, newPositions [i], team.Identifier.ServerRotation) as PlayerHelicopter;
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
						mountPoint.mountable.MountPlayer(passengers[i]);
					}
				}
				ModifyFuel(mini);
				mini.engineController.FinishStartingEngine();
				match.Minicopters.Add(mini);
				owner.UpdateNetworkGroup ();
				owner.UpdateSurroundings ();
				owner.SendEntityUpdate ();	
			}
			if (match.LockedCrate == null)
			{
				Match newMatch = new Match {
					a = match.a,
					b = match.b,
					DeathsTeamA = match.DeathsTeamA,
					DeathsTeamB = match.DeathsTeamB,
					LockedCrate = StartTimedCrate(match),
					TeamsCombined = match.TeamsCombined,
					Ended = match.Ended,
					ID = match.ID,
					Minicopters = match.Minicopters,
					OnRound = match.OnRound,
					PenalizePlayers = match.PenalizePlayers,
					Rounds = match.Rounds,
					Spectators = match.Spectators,
					Started = match.Started,
					TimeLeft = match.TimeLeft,
					Winner = match.Winner,
					IncreaseRoundFlag = match.IncreaseRoundFlag
				};
				match = newMatch;
			}
		}
		private void ModifyFuel(BaseEntity entity)
		{
			StorageContainer container = null;
			BaseVehicle baseVehicle = entity as BaseVehicle;
			if(baseVehicle != null)
			{
				container = baseVehicle.GetFuelSystem()?.fuelStorageInstance.Get(true);
			}
			HotAirBalloon baseBalloon = entity as HotAirBalloon;
			if(baseBalloon != null)
			{
				EntityFuelSystem fuelSystem = baseBalloon.fuelSystem;
				container = fuelSystem?.fuelStorageInstance.Get(true);
			}

			if(container == null)
			{
				return;
			}

			var item = container.inventory.GetSlot (0);
			if (item == null)
			{
				item = ItemManager.CreateByItemID (-946369541, 100); // The amount of lowgrade to add to the vehicle
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
		private void GiveElo(Team Winner, Team Loser){
			foreach (BasePlayer WinTeammate in Winner.TeamMembers){
				//Award(Teammate);
			}
			foreach (BasePlayer LoseTeammate in Loser.TeamMembers){
				//Penalize(Teammate, false);
			}
		}
		
		private void OnPlayerDisconnected(BasePlayer player, string reason){
			if (IsInMatch (player))
			{
				Match match = GetMatch (player);

				if (match.a.TeamMembers.Contains (player))
				{
					if (match.a.TeamMembers.Count < 1 || match.a.TeamMembers == null || (match.Rounds == match.OnRound && match.Ended))
					{
						End (match);
					}
					else if (match.a.TeamMembers.Count > 1){
						match.PenalizePlayers.Add(player);
						match.a.ConnectedMembers.Remove(player);
						match.a.CanForfeit = true;
						SendMessage(match.a, "Your team is available to /forfeit, " + player.displayName.ToString() + " has disconnected from the match and has been penalized for each member of your team.");
					}
				}
				else
				{
					if (match.b.TeamMembers.Count < 1 || match.b.TeamMembers == null || (match.Rounds == match.OnRound))
					{
						End (match);
					}
					else if (match.b.TeamMembers.Count > 1){
						match.PenalizePlayers.Add(player);
						match.b.ConnectedMembers.Remove(player);
						match.b.CanForfeit = true;
						SendMessage(match.b, "Your team is available to /forfeit, " + player.displayName.ToString() + " has disconnected from the match and has been penalized for each member of your team.");
					}
				}
			}
			SwapLeadership(player);
			Kick(player);
		}

		private void SwapLeadership(BasePlayer player){
			// swap leadership if player is teamleader
			if (player.Team != null && player == player.Team.GetLeader ()){
				int rand = Random.Range (0, player.Team.members.Count);
				ulong leaderID = player.Team.members [rand];
				BasePlayer leader = BasePlayer.FindByID (leaderID);
				player.Team.SetTeamLeader (leader.userID);
				leader.Team.RemovePlayer (player.userID);
			}
		}

		private void Kick(BasePlayer player){
			if (player.Team != null && player.Team.members.Count > 1 && player != player.Team.GetLeader()){
				BasePlayer leader = player.Team.GetLeader ();
				leader.Team.RemovePlayer (player.userID);
			}
			else if (player == player.Team.GetLeader() && player.Team.members.Count > 1){
				SwapLeadership(player);
			}
		}

		private void OnEntityDeath(BaseEntity entity, HitInfo hitInfo){
			if (entity == null)
				return;

			var victim = entity as BasePlayer;

			if (victim == null){
				return;
			}
			if (IsInMatch (victim))
			{
				Match match = GetMatch (victim);
				if (match.ID == 0)
					return;
				if (match.Spectators.Contains (victim))
					return;
				if (match.a.TeamMembers.Contains (victim) && !match.DeathsTeamA.Contains (victim))
				{
					match.DeathsTeamA.Add (victim);
				}
				else if (!match.DeathsTeamB.Contains (victim))
				{
					match.DeathsTeamB.Add (victim);
				}
				victim.inventory.Strip ();
				match.Spectators.Add (victim);
				//if (match.DeathsTeamA.Count == match )
				SendSpectate (victim);
			}
			else
			{
				SendHome(victim);
			}
		}
		private void End (Match match){
			if (match.a.RoundsWon > match.b.RoundsWon)
			{
				match.Winner = match.a.Identifier.displayName.ToString ();
			}
			else
			{
				match.Winner = match.b.Identifier.displayName.ToString ();
			}

			if (match.a.RoundsWon == 2)
			{
				Server.Broadcast (match.b.Identifier.displayName.ToString () + " got their shit rocked, 3-0.");
			}
			else if (match.b.RoundsWon == 2)
			{
				Server.Broadcast (match.a.Identifier.displayName.ToString () + " got their shit rocked, 3-0.");
			}
			else
			{
				SendMessage (match.a, match.Winner + " wins. " + match.a.RoundsWon + "-" + match.b.RoundsWon + ".");
				SendMessage (match.b, match.Winner + " wins. " + match.b.RoundsWon + "-" + match.a.RoundsWon + ".");
			}
			
			foreach (BasePlayer player in match.TeamsCombined)
			{
				SendHome (player);
			}

			foreach (PlayerHelicopter mini in match.Minicopters)
			{
				if (mini != null || !mini.IsDestroyed)
				{
					mini.Kill ();
				}
			}

			match.Minicopters.Clear ();
			match.Spectators.Clear ();
			match.DeathsTeamA.Clear ();
			match.DeathsTeamB.Clear ();
			match.a.TeamMembers.Clear ();
			match.b.TeamMembers.Clear ();
			match.TeamsCombined.Clear ();
			match.Ended = true;
			match.Started = false;
			match.ID = 0;
			match.OnRound = 0;
			match.Rounds = 0;
			match.Winner = null;
			if (match.LockedCrate != null)
			{
				match.LockedCrate.Kill ();
				match.LockedCrate = null;
			}

			if (match.PenalizePlayers.Count > 0)
			{
				foreach (BasePlayer rat in match.PenalizePlayers)
				{
					Penalize (rat, true, match.a.TeamMembers.Count);		
				}
			}
			match.PenalizePlayers.Clear ();

			if (Teams.ContainsKey (match.a))
			{
				Teams.Remove (match.a);
			}
			if (Teams.ContainsKey (match.b))
			{
				Teams.Remove (match.b);
			}
			Matches.Remove (match);
		}
		private void SendHome(BasePlayer player){
			Vector3 spawn;
			spawn.x = -140.41f;
			spawn.y = 20.21f;
			spawn.z = -321.54f;
			player.inventory.Strip ();
			player.inventory.containerWear.SetLocked (false);
			player.Respawn ();
			Teleport(player, spawn);
			DeMetabolize (player);
		}

		private void SendSpectate (BasePlayer player){
			Vector3 deathLobby;
			deathLobby.x = -119.57f;
			deathLobby.y = 15.11f;
			deathLobby.z = 103.78f;

			/*
			deathLobby.x = -417.66f;
			deathLobby.y = 20.08f;
			deathLobby.z = -226.21f;
			*/
			Teleport (player, deathLobby);
		}
		private bool IsInMatch(BasePlayer player){
			foreach (Match match in Matches){
				foreach (BasePlayer Entry in match.TeamsCombined){
					if (Entry == player && match.ID != 0 && player != null){
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
			if (multiplier != 0){

			}
			else{

			}
		}

		private Match GetMatch(BasePlayer player){
			Match matchNotFound = new Match();
			foreach (Match match in Matches){
				foreach (BasePlayer Entry in match.TeamsCombined){
					if (Entry == player){
						return match;
					}
				}
				MatchSet(matchNotFound, match);
			}
			matchNotFound.ID = 0;
			return matchNotFound;
		}

		/*
		private int GetMatch (HackableLockedCrate crate)
		{
			for (int i = 0; i < Matches.Count; i++)
			{
				if (Matches[i].crateKek == crate)
				{
					return i;
				}
			}
			return 1000;
		}
		*/

		private void MatchSet(Match receiver, Match setter){
			receiver.a = setter.a;
			receiver.b = setter.b;
			receiver.Started = setter.Started;
			receiver.OnRound = setter.OnRound;
			receiver.Rounds = setter.Rounds;
			receiver.Ended = setter.Ended;
			receiver.ID = setter.ID; // Unique key based on the leaders of both teams' game IDs
			receiver.DeathsTeamA = setter.DeathsTeamA;
			receiver.DeathsTeamB = setter.DeathsTeamB;
			receiver.Spectators = setter.Spectators;
			receiver.TeamsCombined = setter.TeamsCombined;
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

		/*
		void OnCrateHackEnd (HackableLockedCrate self)
		{
			return;
			if (self != null)
			{
				Match match = new Match ();
				match = Matches[GetMatch(self)];

				if (match.ID == 0)
				{
					Server.Broadcast ("invalid match ");
				}

				if (!match.RoundOver)
				{
					//SendMessage (match.b, "<color=#316bf5>BLUE</color> won the round by successully expiring the crate time.");
					//SendMessage (match.a, "<color=#316bf5>BLUE</color> won the round by successully expiring the crate time.");
					self.Kill ();
					
					match.IncreaseRoundFlag = true;
					match.Ended = true;
					//RoundIncrease (match);
				}
			}
			else
			{
				Server.Broadcast ("null");
			}
			
		}
		*/
		private void OnPlayerConnected (BasePlayer player)
		{
			if (!player.IsValid())
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
			
			foreach (KeyValuePair<BasePlayer, float> disconnect in DisconnectedPlayersAlert){
				if (player == disconnect.Key)
					SendReply(disconnect.Key, "Nice disconnect during the last match you played; you were penalized an extra " + disconnect.Value + " elo for leaving.");
			}
		}

		private void AwardPoints (Match match)
		{
			return;

			int miniCount = 0;

			List<PlayerHelicopter> MinicoptersDestroyed = new List<PlayerHelicopter> ();


			if (match.b.Color)
			{
				match.b.Points += match.b.CrateHackWins * 15;
			}
			else
			{
				if (match.b.TeamMembers.Count % 2 == 0)
				{
					miniCount = match.b.TeamMembers.Count / 2;
				}
				else
				{
					miniCount = (match.b.TeamMembers.Count / 2) + 1;
				}

				foreach (PlayerHelicopter mini in match.Minicopters)
				{
					if (mini == null || mini.IsDestroyed)
					{
						MinicoptersDestroyed.Add (mini);
					}
				}

				if (match.Minicopters.Count != miniCount)
				{
					match.b.Points += (miniCount - MinicoptersDestroyed.Count) * -15;
				}
			}


			if (match.a.Color)
			{
				match.a.Points += match.a.CrateHackWins * 15;
			}
			else
			{
				if (match.a.TeamMembers.Count % 2 == 0)
				{
					miniCount = match.a.TeamMembers.Count / 2;
				}
				else
				{
					miniCount = (match.a.TeamMembers.Count / 2) + 1;
				}

				foreach (PlayerHelicopter mini in match.Minicopters)
				{
					if (mini == null || mini.IsDestroyed)
					{
						MinicoptersDestroyed.Add (mini);
					}
				}

				if (match.Minicopters.Count != miniCount)
				{
					match.a.Points += (miniCount - MinicoptersDestroyed.Count) * -15;
				}
			}
		}

		private void SubscribeHooks(bool flag){
			if (flag){
				Subscribe (nameof (OnPlayerConnected));
				Subscribe (nameof (OnPlayerDisconnected));
				Subscribe (nameof (OnEntityDeath));
				Subscribe (nameof (OnTeamKick));
				Subscribe (nameof (OnTeamLeave));
				//Subscribe (nameof (OnCrateHackEnd));
			}
			else{
				Unsubscribe (nameof (OnPlayerConnected));
				Unsubscribe (nameof (OnPlayerDisconnected));
				Unsubscribe (nameof (OnEntityDeath));
				Unsubscribe (nameof (OnTeamKick));
				Unsubscribe (nameof (OnTeamLeave));
				//Unsubscribe (nameof (OnCrateHackEnd));
			}
		}
		#endregion
	}
}