﻿using Latest_Chatty_8.Shared.Settings;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Latest_Chatty_8.Views
{
	public sealed partial class SettingsView : ShellView
	{
		public override string ViewTitle
		{
			get
			{
				return "Settings";
			}
		}

		public SettingsView ()
		{
            this.InitializeComponent();
			this.DataContext = LatestChattySettings.Instance;
			this.password.Password = LatestChattySettings.Instance.Password;
			this.userValidation.DataContext = this;
		}

		private string userAsyncToken;

		private bool npcValidatingUser;
		public bool ValidatingUser
		{
			get { return npcValidatingUser; }
			set { this.SetProperty(ref this.npcValidatingUser, value); }
		}

		private bool npcValidUser;
		public bool ValidUser
		{
			get { return npcValidUser; }
			set { this.SetProperty(ref this.npcValidUser, value); }
		}

		private bool npcInvalidUser;
		public bool InvalidUser
		{
			get { return npcInvalidUser; }
			set { this.SetProperty(ref this.npcInvalidUser, value); }
		}

		private bool npcSyncingSettings;
		public bool SyncingSettings
		{
			get { return npcSyncingSettings; }
			set { this.SetProperty(ref this.npcSyncingSettings, value); }
		}

		public void Initialize()
		{
			this.ValidateUser(false);
		}

		async private void LogOutClicked(object sender, RoutedEventArgs e)
		{
			LatestChattySettings.Instance.Username = LatestChattySettings.Instance.Password = this.password.Password = string.Empty;
			await CoreServices.Instance.AuthenticateUser();
			await LatestChattySettings.Instance.LoadLongRunningSettings();
			await CoreServices.Instance.RefreshChatty();
		}

		private void PasswordChanged(object sender, RoutedEventArgs e)
		{
			LatestChattySettings.Instance.Password = ((PasswordBox)sender).Password;
			this.ValidateUser(true);
		}

		private void UserNameChanged(object sender, TextChangedEventArgs e)
		{
			this.ValidateUser(true);
		}

		private async void ValidateUser(bool updateCloudSettings)
		{
			this.ValidatingUser = true;
			this.InvalidUser = false;
			this.ValidUser = false;
			this.userAsyncToken = Guid.NewGuid().ToString();

			try
			{
				var validResponse = await CoreServices.Instance.AuthenticateUser(this.userAsyncToken);

				if (this.userAsyncToken == validResponse.Item2)
				{
					if (validResponse.Item1)
					{
						if (updateCloudSettings)
						{
							this.SyncingSettings = true;
							await LatestChattySettings.Instance.LoadLongRunningSettings();
							await CoreServices.Instance.RefreshChatty();
							this.SyncingSettings = false;
						}
						this.ValidatingUser = false;
						this.ValidUser = true;
					}
					else
					{
						this.ValidatingUser = false;
						this.InvalidUser = true;
					}
				}
			}
			catch
			{ System.Diagnostics.Debug.Assert(false); }
		}
	}
}
