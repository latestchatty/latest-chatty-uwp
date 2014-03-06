﻿using Latest_Chatty_8.Settings;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using Latest_Chatty_8.Networking;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using System;
using System.IO;
using Latest_Chatty_8.Common;
using Newtonsoft.Json.Linq;
using Latest_Chatty_8.DataModel;
using System.Threading;
using System.Collections.ObjectModel;
using Windows.UI.Core;

namespace Latest_Chatty_8
{
	/// <summary>
	/// Singleton object to perform some common functionality across the entire application
	/// </summary>
	public class CoreServices : BindableBase
	{
		#region Singleton
		private static CoreServices _coreServices = null;
		public static CoreServices Instance
		{
			get
			{
				if (_coreServices == null)
				{
					_coreServices = new CoreServices();
				}
				return _coreServices;
			}
		}
		#endregion

		private bool initialized = false;
		private CancellationTokenSource cancelChattyRefreshSource;

		async public Task Initialize()
		{
			if (!this.initialized)
			{
				this.initialized = true;
				this.chatty = new ObservableCollection<Comment>();
				this.Chatty = new ReadOnlyObservableCollection<Comment>(this.chatty);
				this.PostCounts = (await ComplexSetting.ReadSetting<Dictionary<int, int>>("postcounts")) ?? new Dictionary<int, int>();
				await this.AuthenticateUser();
				await LatestChattySettings.Instance.LoadLongRunningSettings();
				await this.RefreshChatty();
			}
		}

		/// <summary>
		/// Suspends this instance.
		/// </summary>
		async public Task Suspend()
		{
			if (this.PostCounts != null)
			{
				if (this.PostCounts.Count > 50000)
				{
					this.PostCounts = this.PostCounts.Skip(this.PostCounts.Count - 50000) as Dictionary<int, int>;
				}
				ComplexSetting.SetSetting<Dictionary<int, int>>("postcounts", this.PostCounts);
			}
			await LatestChattySettings.Instance.SaveToCloud();
			//this.PostCounts = null;
			//GC.Collect();
		}

		/// <summary>
		/// Gets the credentials for the currently logged in user.
		/// </summary>
		/// <value>
		/// The credentials.
		/// </value>
		private NetworkCredential credentials = null;
		public NetworkCredential Credentials
		{
			get
			{
				if (this.credentials == null)
				{
					this.credentials = new NetworkCredential(LatestChattySettings.Instance.Username, LatestChattySettings.Instance.Password);
				}
				return this.credentials;
			}
		}

		/// <summary>
		/// The post counts
		/// </summary>
		public Dictionary<int, int> PostCounts;

		/// <summary>
		/// Gets set to true when a reply was posted so we can refresh the thread upon return.
		/// </summary>
		public bool PostedAComment { get; set; }

		/// <summary>
		/// Clears the tile and optionally registers for notifications if necessary.
		/// </summary>
		/// <param name="registerForNotifications">if set to <c>true</c> [register for notifications].</param>
		/// <returns></returns>
		async public Task ClearTile(bool registerForNotifications)
		{
			TileUpdateManager.CreateTileUpdaterForApplication().Clear();
			BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
			if (registerForNotifications)
			{
				await NotificationHelper.ReRegisterForNotifications();
			}
		}

		private bool npcLoggedIn;
		/// <summary>
		/// Gets a value indicating whether there is a currently logged in (and authenticated) user.
		/// </summary>
		/// <value>
		///   <c>true</c> if [logged in]; otherwise, <c>false</c>.
		/// </value>
		public bool LoggedIn
		{
			get { return npcLoggedIn; }
			private set
			{
				this.SetProperty(ref this.npcLoggedIn, value);
			}
		}

		private List<Comment> npcChatty;
		/// <summary>
		/// Gets the active chatty
		/// </summary>
		public ReadOnlyObservableCollection<Comment> Chatty
		{
			get;
			private set;
		}

		private ObservableCollection<Comment> chatty;

		/// <summary>
		/// Forces a full refresh of the chatty.
		/// </summary>
		/// <returns></returns>
		public async Task RefreshChatty()
		{
			this.StopAutoChattyRefresh();
			var latestEventJson = await JSONDownloader.Download(Networking.Locations.GetNewestEventId);
			this.lastEventId = (int)latestEventJson["eventId"];
			var chattyJson = await JSONDownloader.Download(Networking.Locations.Chatty);
			var parsedChatty = CommentDownloader.ParseChatty(chattyJson);
			this.chatty.Clear();
			foreach (var comment in parsedChatty)
			{
				this.chatty.Add(comment);
			}
			this.StartAutoChattyRefresh();
		}

		private int lastEventId = 0;
		public void StartAutoChattyRefresh()
		{
			if (this.cancelChattyRefreshSource == null)
			{
				this.cancelChattyRefreshSource = new CancellationTokenSource();

				var ct = this.cancelChattyRefreshSource.Token;
				Task.Factory.StartNew(async () =>
				{
					while (!ct.IsCancellationRequested)
					{
						try
						{
							var events = await JSONDownloader.Download(Networking.Locations.WaitForEvent + "?lastEventId=" + this.lastEventId);
							this.lastEventId = (int)events["lastEventId"];
							System.Diagnostics.Debug.WriteLine("Event Data: {0}", events.ToString());
							foreach (var e in events["events"])
							{
								switch ((string)e["eventType"])
								{
									case "newPost":
										var newPostJson = e["eventData"]["post"];
										var threadRootId = (int)newPostJson["threadId"];
										var parentId = (int)newPostJson["parentId"];
										if (parentId == 0)
										{
											//Brand new post.
											//Parse it and add it to the top.
											var newComment = CommentDownloader.ParseCommentFromJson(newPostJson, null, null);
											//:TODO: Shouldn't have to do this.
											newComment.IsNew = newComment.HasNewReplies = true;
											this.chatty.Insert(0, newComment);
										}
										else
										{
											var threadRootComment = this.chatty.SingleOrDefault(c => c.Id == threadRootId);
											if (threadRootComment != null)
											{
												var parentComment = threadRootComment.FlattenedComments.SingleOrDefault(c => c.Id == parentId);
												if (parentComment != null)
												{
													var newComment = CommentDownloader.ParseCommentFromJson(newPostJson, parentComment, threadRootComment.Author);
													await threadRootComment.AddReply(newComment);
													//threadRootComment.ReplyCount = threadRootComment.FlattenedComments.Count();
												}
											}
											var currentIndex = this.chatty.IndexOf(threadRootComment);
											await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
											{
												this.chatty.Move(currentIndex, 0);
											});
										}
										break;
									case "nuked":
										break;

								}
							}
						}
						catch (Exception e)
						{
							//:TODO: Do I just want to swallow all exceptions?  Probably.  Everything should continue to function alright, we just won't "push" update.
						}
					}
				}, ct);
			}
		}

		public void StopAutoChattyRefresh()
		{
			if (this.cancelChattyRefreshSource != null)
			{
				this.cancelChattyRefreshSource.Cancel();
			}
		}

		/// <summary>
		/// Authenticates the user set in the application settings.
		/// </summary>
		/// <param name="token">A token that can be used to identify a result.</param>
		/// <returns></returns>
		public async Task<Tuple<bool, string>> AuthenticateUser(string token = "")
		{
			var result = false;
			this.credentials = null;
			var request = (HttpWebRequest)HttpWebRequest.Create("https://www.shacknews.com/account/signin");
			request.Method = "POST";
			request.Headers["x-requested-with"] = "XMLHttpRequest";
			request.Headers[HttpRequestHeader.Pragma] = "no-cache";

			request.ContentType = "application/x-www-form-urlencoded";

			var requestStream = await request.GetRequestStreamAsync();
			var streamWriter = new StreamWriter(requestStream);
			streamWriter.Write(String.Format("user-identifier={0}&supplied-pass={1}&get_fields[]=result", Uri.EscapeUriString(CoreServices.Instance.Credentials.UserName), Uri.EscapeUriString(CoreServices.Instance.Credentials.Password)));
			streamWriter.Flush();
			streamWriter.Dispose();
			var response = await request.GetResponseAsync() as HttpWebResponse;
			//Doesn't seem like the API is actually returning failure codes, but... might as well handle it in case it does some time.
			if (response.StatusCode == HttpStatusCode.OK)
			{
				using (var responseStream = new StreamReader(response.GetResponseStream()))
				{
					var data = await responseStream.ReadToEndAsync();
					System.Diagnostics.Debug.WriteLine("Response {0}", data);
					try
					{
						var jsonResult = JObject.Parse(data)["result"];
						result = jsonResult["valid"].ToString().Equals("true");
					}
					catch
					{
						result = false;
					}

				}
			}

			if (!result)
			{
				if (LatestChattySettings.Instance.CloudSync)
				{
					LatestChattySettings.Instance.CloudSync = false;
				}
				if (LatestChattySettings.Instance.EnableNotifications)
				{
					await NotificationHelper.UnRegisterNotifications();
				}
				LatestChattySettings.Instance.ClearPinnedComments();
			}

			this.LoggedIn = result;
			return new Tuple<bool, string>(result, token);
		}
	}
}

