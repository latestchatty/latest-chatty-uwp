﻿using Autofac;
using Latest_Chatty_8.Common;
using Latest_Chatty_8.Settings;
using Latest_Chatty_8.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Latest_Chatty_8
{
	public sealed partial class Shell : Page, INotifyPropertyChanged
	{
		#region NPC
		/// <summary>
		/// Multicast event for property change notifications.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>
		/// Checks if a property already matches a desired value.  Sets the property and
		/// notifies listeners only when necessary.
		/// </summary>
		/// <typeparam name="T">Type of the property.</typeparam>
		/// <param name="storage">Reference to a property with both getter and setter.</param>
		/// <param name="value">Desired value for the property.</param>
		/// <param name="propertyName">Name of the property used to notify listeners.  This
		/// value is optional and can be provided automatically when invoked from compilers that
		/// support CallerMemberName.</param>
		/// <returns>True if the value was changed, false if the existing value matched the
		/// desired value.</returns>
		private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] String propertyName = null)
		{
			if (object.Equals(storage, value)) return false;

			storage = value;
			this.OnPropertyChanged(propertyName);
			return true;
		}

		/// <summary>
		/// Notifies listeners that a property value has changed.
		/// </summary>
		/// <param name="propertyName">Name of the property used to notify listeners.  This
		/// value is optional and can be provided automatically when invoked from compilers
		/// that support <see cref="CallerMemberNameAttribute"/>.</param>
		private void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			var eventHandler = this.PropertyChanged;
			if (eventHandler != null)
			{
				eventHandler(this, new PropertyChangedEventArgs(propertyName));
			}
		}
		#endregion

		#region Private Variables
		IContainer container;
		Uri embeddedBrowserLink;
		ShellView currentlyDisplayedView;
		CoreWindow keyBindingWindow;
		WebView embeddedBrowser;
		#endregion

		private string npcCurrentViewName = "";
		public string CurrentViewName
		{
			get { return npcCurrentViewName; }
			set { this.SetProperty(ref this.npcCurrentViewName, value); }
		}


		private ChattyManager npcChattyManager;
		public ChattyManager ChattyManager
		{
			get { return this.npcChattyManager; }
			set { this.SetProperty(ref this.npcChattyManager, value); }
		}

		private MessageManager npcMessageManager;
		public MessageManager MessageManager
		{
			get { return this.npcMessageManager; }
			set { this.SetProperty(ref this.npcMessageManager, value); }
		}

		private AuthenticationManager npcAuthManager;
		public AuthenticationManager AuthManager
		{
			get { return this.npcAuthManager; }
			set { this.SetProperty(ref this.npcAuthManager, value); }
		}

		private LatestChattySettings npcSettings;
		public LatestChattySettings Settings
		{
			get { return this.npcSettings; }
			set { this.SetProperty(ref this.npcSettings, value); }
		}

		#region Constructor
		public Shell(Frame rootFrame, IContainer container)
		{
			this.InitializeComponent();

			ApplicationView.GetForCurrentView().SetPreferredMinSize(new Windows.Foundation.Size(320, 320));

			if (rootFrame.Content is Chatty)
			{
				this.chattyRadio.IsChecked = true;
				((Chatty)rootFrame.Content).LinkClicked += Sv_LinkClicked;
				((Chatty)rootFrame.Content).ShellMessage += Sv_ShellMessage;
			}
			this.splitter.Content = rootFrame;
			rootFrame.Navigated += FrameNavigatedTo;
			rootFrame.Navigating += FrameNavigating;
			this.container = container;
			this.MessageManager = this.container.Resolve<MessageManager>();
			this.AuthManager = this.container.Resolve<AuthenticationManager>();
			this.Settings = this.container.Resolve<LatestChattySettings>();
			this.ChattyManager = this.container.Resolve<ChattyManager>();
			this.Settings.PropertyChanged += Settings_PropertyChanged;

			this.SetThemeColor();

			var sv = rootFrame.Content as ShellView;
			if (sv != null)
			{
				this.currentlyDisplayedView = sv;
				SetCaptionFromFrame(sv);
			}

			Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += (
				(o, a) =>
				{
					(new Microsoft.ApplicationInsights.TelemetryClient()).TrackEvent("Shell-HardwareBackButtonPressed");
					if (this.embeddedBrowserLink != null)
					{
						if (this.embeddedViewer.Visibility == Windows.UI.Xaml.Visibility.Visible)
						{
							if (this.embeddedBrowser.CanGoBack)
							{
								this.embeddedBrowser.GoBack();
							}
							else
							{
								this.CloseEmbeddedBrowser();
							}
						}
					}
					a.Handled = GoBack();
				});
		}


		private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals("ThemeName", StringComparison.OrdinalIgnoreCase))
			{
				this.SetThemeColor();
			}
		}
		private void FrameNavigating(object sender, NavigatingCancelEventArgs e)
		{
			if (this.currentlyDisplayedView != null)
			{
				this.currentlyDisplayedView.LinkClicked -= Sv_LinkClicked;
				this.currentlyDisplayedView.ShellMessage -= Sv_ShellMessage;
				this.currentlyDisplayedView = null;
			}
		}

		private void FrameNavigatedTo(object sender, NavigationEventArgs e)
		{
			var sv = e.Content as ShellView;
			if (sv != null)
			{
				this.currentlyDisplayedView = sv;
				sv.LinkClicked += Sv_LinkClicked;
				sv.ShellMessage += Sv_ShellMessage;
				SetCaptionFromFrame(sv);
			}
		}

		async private void Sv_ShellMessage(object sender, ShellMessageEventArgs e)
		{
			await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				this.FindName("messageContainer");
			});
			this.popupMessage.ShowMessage(e);
		}

		private void Sv_LinkClicked(object sender, LinkClickedEventArgs e)
		{
			this.ShowEmbeddedLink(e.Link);
		}

		#endregion

		private void ClickedNav(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			var f = this.splitter.Content as Frame;
			if (this.chattyRadio.IsChecked.HasValue && this.chattyRadio.IsChecked.Value)
			{
				f.Navigate(typeof(Chatty), this.container);
			}
			else if (this.settingsRadio.IsChecked.HasValue && this.settingsRadio.IsChecked.Value)
			{
				f.Navigate(typeof(SettingsView), this.container);
			}
			else if (this.messagesRadio.IsChecked.HasValue && this.messagesRadio.IsChecked.Value)
			{
				f.Navigate(typeof(Messages), this.container);
			}
			else if (this.helpRadio.IsChecked.HasValue && this.helpRadio.IsChecked.Value)
			{
				f.Navigate(typeof(Help), this.container);
			}
			//else if (this.tagRadio.IsChecked.HasValue && this.tagRadio.IsChecked.Value)
			//{
			//	f.Navigate(typeof(TagView), this.container);
			//}
			this.BurguerToggle.IsChecked = false;
		}



		private void SetCaptionFromFrame(ShellView sv)
		{
			this.CurrentViewName = sv.ViewTitle;
		}

		private void SetThemeColor()
		{
			var titleBar = ApplicationView.GetForCurrentView().TitleBar;
			titleBar.ButtonBackgroundColor = titleBar.BackgroundColor = titleBar.InactiveBackgroundColor = titleBar.ButtonInactiveBackgroundColor = this.Settings.Theme.WindowTitleBackgroundColor;
			titleBar.ButtonForegroundColor = titleBar.ForegroundColor = this.Settings.Theme.WindowTitleForegroundColor;
			titleBar.InactiveForegroundColor = titleBar.ButtonInactiveForegroundColor = this.Settings.Theme.WindowTitleForegroundColorInactive;
		}

		private void AcknowledgeUpdateInfoClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			(new Microsoft.ApplicationInsights.TelemetryClient()).TrackEvent("Shell-AcknowledgedUpgradeInfo");
			this.Settings.MarkUpdateInfoRead();
			this.updateInfoAvailableButton.Flyout.Hide();
		}

		private bool GoBack()
		{
			var f = this.splitter.Content as Frame;
			if (f.CanGoBack)
			{
				f.GoBack();
				return true;
			}
			return false;
		}

		async private void ShowEmbeddedLink(Uri link)
		{
			if (await this.LaunchExternalAppForUrlHandlerIfNecessary(link))
			{
				return;
			}

			var embeddedHtml = EmbedHelper.GetEmbedHtml(link);

			if (string.IsNullOrWhiteSpace(embeddedHtml) && !this.Settings.OpenUnknownLinksInEmbeddedBrowser)
			{
				//Don't want to use the embedded browser, ever.
				await Launcher.LaunchUriAsync(link);
				return;
			}

			this.FindName("embeddedViewer");
			(new Microsoft.ApplicationInsights.TelemetryClient()).TrackEvent("ShellEmbeddedBrowserShown");
			this.embeddedBrowser = new WebView(WebViewExecutionMode.SeparateThread);
			this.embeddedBrowserContainer.Children.Add(this.embeddedBrowser);
			this.embeddedViewer.Visibility = Windows.UI.Xaml.Visibility.Visible;
			this.embeddedBrowserLink = link;
			this.keyBindingWindow = CoreWindow.GetForCurrentThread();
			this.keyBindingWindow.KeyDown += Shell_KeyDown;
			this.embeddedBrowser.NavigationStarting += EmbeddedBrowser_NavigationStarting;
			this.embeddedBrowser.NavigationCompleted += EmbeddedBrowser_NavigationCompleted;
			if (!string.IsNullOrWhiteSpace(embeddedHtml))
			{
				this.embeddedBrowser.NavigateToString(embeddedHtml);
			}
			else
			{
				this.embeddedBrowser.Navigate(link);
			}
		}

		private void EmbeddedBrowser_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
		{
			this.browserLoadingIndicator.Visibility = Windows.UI.Xaml.Visibility.Visible;
			this.browserLoadingIndicator.IsActive = true;
		}

		private void EmbeddedBrowser_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
		{
			this.browserLoadingIndicator.IsActive = false;
			this.browserLoadingIndicator.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
		}

		private void Shell_KeyDown(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs args)
		{
			switch (args.VirtualKey)
			{
				case VirtualKey.Escape:
					if (this.embeddedViewer.Visibility == Windows.UI.Xaml.Visibility.Visible)
					{
						this.CloseEmbeddedBrowser();
					}
					break;
				default:
					break;
			}
		}

		async private Task<bool> LaunchExternalAppForUrlHandlerIfNecessary(Uri link)
		{
			var launchUri = AppLaunchHelper.GetAppLaunchUri(this.Settings, link);
			if (launchUri != null)
			{
				await Launcher.LaunchUriAsync(launchUri);
				return true;
			}
			return false;
		}

		private void EmbeddedCloseClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			this.CloseEmbeddedBrowser();
		}

		private void CloseEmbeddedBrowser()
		{
			(new Microsoft.ApplicationInsights.TelemetryClient()).TrackEvent("ShellEmbeddedBrowserClosed");
			this.keyBindingWindow.KeyDown -= Shell_KeyDown;
			this.embeddedBrowser.NavigationStarting -= EmbeddedBrowser_NavigationStarting;
			this.embeddedBrowser.NavigationCompleted -= EmbeddedBrowser_NavigationCompleted;
			this.embeddedViewer.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
			this.embeddedBrowser.Stop();
			this.embeddedBrowser.NavigateToString("");
			this.embeddedBrowserContainer.Children.Remove(this.embeddedBrowser);
			this.embeddedBrowser = null;
			this.embeddedBrowserLink = null;
		}

		async private void EmbeddedBrowserClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
		{
			if (this.embeddedBrowserLink != null)
			{
				(new Microsoft.ApplicationInsights.TelemetryClient()).TrackEvent("ShellEmbeddedBrowserShowFullBrowser");
				await Windows.System.Launcher.LaunchUriAsync(this.embeddedBrowserLink);
				this.CloseEmbeddedBrowser();
			}
		}
	}
}
