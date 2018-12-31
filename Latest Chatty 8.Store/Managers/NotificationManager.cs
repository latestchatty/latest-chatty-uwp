﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.PushNotifications;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Common;
using Latest_Chatty_8.Settings;

namespace Latest_Chatty_8.Managers
{
	public class NotificationManager : BaseNotificationManager, IDisposable
	{
		private PushNotificationChannel _channel;
		private readonly LatestChattySettings _settings;
		private bool _suppressNotifications = true;

		private readonly SemaphoreSlim _locker = new SemaphoreSlim(1);

		//private SemaphoreSlim removalLocker = new SemaphoreSlim(1);
		//bool processingRemovalQueue = false;

		public int InitializePriority => int.MaxValue;

		public NotificationManager(LatestChattySettings settings, AuthenticationManager authManager)
		: base(authManager)
		{
			_settings = settings;
			_settings.PropertyChanged += Settings_PropertyChanged;
			Window.Current.Activated += Window_Activated;
		}

		#region Register
		public async override Task UnRegisterNotifications()
		{
			try
			{
				var client = new HttpClient();
				var data = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					{ "deviceId", _settings.NotificationId.ToString() }
				});
				client.DefaultRequestHeaders.Add("Accept", "application/json");
				using (var _ = await client.PostAsync(Locations.NotificationDeRegister, data)) { }

				//TODO: Test response.

				//I guess there's nothing to do with WNS
			}
			catch (Exception)
			{
				//(new TelemetryClient()).TrackException(e);
				//System.Diagnostics.Debugger.Break();
			}
		}

		/// <summary>
		/// Unbinds, closes, and rebinds notification channel.
		/// </summary>
		public async override Task ReRegisterForNotifications()
		{
			await UnRegisterNotifications();
			await RegisterForNotifications();
		}

		/// <summary>
		/// Registers for notifications if not already registered.
		/// </summary>
		public async override Task RegisterForNotifications()
		{
			if (!AuthManager.LoggedIn || !_settings.EnableNotifications) return;

			try
			{
				await _locker.WaitAsync();
				_channel = await PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync();
				if (_channel != null)
				{
					NotificationLog($"Re-bound notifications to Uri: {_channel.Uri}");
					_channel.PushNotificationReceived += Channel_PushNotificationReceived;
					await NotifyServerOfUriChange();
				}
			}
			catch (Exception)
			{
				//(new TelemetryClient()).TrackException(e);
				//System.Diagnostics.Debugger.Break();
			}
			finally
			{
				_locker.Release();
			}
		}

		#endregion

		//ConcurrentQueue<int> notificationRemovals = new ConcurrentQueue<int>();

		public override void RemoveNotificationForCommentId(int postId)
		{
			//if (this.notificationRemovals.Contains(postId)) return;
			//this.notificationRemovals.Enqueue(postId);
			//Task.Run(() => ProcessRemovalQueue());
		}

		//private void ProcessRemovalQueue()
		//{
		//	this.removalLocker.Wait();
		//	if (this.processingRemovalQueue)
		//	{
		//		this.removalLocker.Release();
		//		return;
		//	}
		//	this.processingRemovalQueue = true;
		//	this.removalLocker.Release();

		//	try
		//	{
		//		int postId;

		//		while (this.notificationRemovals.TryDequeue(out postId))
		//		{
		//			ToastNotificationManager.History.Remove(postId.ToString(), "ReplyToUser");
		//			System.Diagnostics.Debug.WriteLine("Notification Queue Count: " + this.notificationRemovals.Count);
		//		}
		//	}
		//	finally
		//	{
		//		this.removalLocker.Wait();
		//		this.processingRemovalQueue = false;
		//		this.removalLocker.Release();
		//	}
		//}

		public async override Task<NotificationUser> GetUser()
		{
			try
			{
				if (!AuthManager.LoggedIn || !_settings.EnableNotifications) return null;

				var response = await JsonDownloader.Download(Locations.GetNotificationUserUrl(AuthManager.UserName));
				var user = response.ToObject<NotificationUser>();
				return user;
			}
			catch (Exception)
			{
				//(new TelemetryClient()).TrackException(e);
				//System.Diagnostics.Debugger.Break();
			}
			return null;
		}

		#region Helper Methods

		private void NotificationLog(string formatMessage, params object[] args)
		{
			Debug.WriteLine("NOTIFICATION - " + formatMessage, args);
		}

		private async Task NotifyServerOfUriChange()
		{
			if (!AuthManager.LoggedIn) return;

			var client = new HttpClient();
			var data = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					{ "deviceId", _settings.NotificationId.ToString() },
					{ "userName", AuthManager.UserName },
					{ "channelUri", _channel.Uri }
				});
			client.DefaultRequestHeaders.Add("Accept", "application/json");
			using (await client.PostAsync(Locations.NotificationRegister, data)) { }

			data = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				{ "userName", AuthManager.UserName },
				{ "notifyOnUserName", _settings.NotifyOnNameMention ? "1" : "0" }
			});
			using (await client.PostAsync(Locations.NotificationUser, data)) { }
		}
		#endregion

		#region Events
		private void Channel_PushNotificationReceived(PushNotificationChannel sender, PushNotificationReceivedEventArgs args)
		{
			bool suppress;

			int postId = -1;

			if (args.NotificationType != PushNotificationType.Badge)
			{
				suppress = _suppressNotifications; //Cancel all notifications if the application is active.

				if (postId > 0 && suppress)
				{
					var jThread = JsonDownloader.Download($"{Locations.GetThread}?id={postId}").Result;

					DateTime minDate = DateTime.MaxValue;
					if (jThread != null && jThread["threads"] != null)
					{
						foreach (var post in jThread["threads"][0]["posts"])
						{
							var date = DateTime.Parse(post["date"].ToString(), null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
							if (date < minDate)
							{
								minDate = date;
							}
						}
						if (minDate.AddHours(18).Subtract(DateTime.UtcNow).TotalSeconds < 0)
						{
							suppress = false; //Still want to show the notification if the thread is expired.
						}
					}
				}
				args.Cancel = suppress;
			}
		}

		private void Window_Activated(object sender, WindowActivatedEventArgs e)
		{
			_suppressNotifications = e.WindowActivationState != CoreWindowActivationState.Deactivated;
			if (_suppressNotifications)
			{
				Debug.WriteLine("Suppressing notifications.");
			}
			else
			{
				Debug.WriteLine("Allowing notifications.");
			}
		}

		private async void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(nameof(LatestChattySettings.EnableNotifications)) ||
				e.PropertyName.Equals(nameof(LatestChattySettings.NotifyOnNameMention)))
			{
				await ReRegisterForNotifications();
			}
		}

		#endregion

		#region IDisposable Support
		private bool _disposedValue; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					// TODO: dispose managed state (managed objects).
					_locker?.Dispose();
					//this.removalLocker?.Dispose();
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				_disposedValue = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~NotificationManager() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion
	}
}
