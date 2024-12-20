﻿using Autofac;
using Common;
using Newtonsoft.Json;
using System;
using Werd.Common;
using Werd.Settings;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

// The Basic Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234237

namespace Werd.Views
{
	/// <summary>
	/// A basic page that provides characteristics common to most applications.
	/// </summary>
	public sealed partial class Help
	{
		private readonly string _appName;
		private readonly string _version;
		private AppSettings _settings;
		public override event EventHandler<LinkClickedEventArgs> LinkClicked = delegate { }; //Unused
		public override event EventHandler<ShellMessageEventArgs> ShellMessage = delegate { };

		public Help()
		{
			InitializeComponent();
			_appName = Package.Current.DisplayName;
			var version = Package.Current.Id.Version;
			_version = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

			AppNameTextArea.Text = _appName;
			VersionTextArea.Text = _version;
		}

		public override string ViewIcons { get => "\uE897"; set { return; } }
		public override string ViewTitle { get => "Help/About"; set { return; } }


		protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			base.OnNavigatedTo(e);
			var p = e.Parameter as Tuple<IContainer, bool>;
			var container = p?.Item1;
			_settings = container.Resolve<AppSettings>();
			if (p != null && p.Item2)
			{
				Pivot.SelectedIndex = 1;
			}
		}

		private async void VersionDoubleClicked(object sender, DoubleTappedRoutedEventArgs e)
		{
			var serializedSettings = JsonConvert.SerializeObject(_settings);
			var dialog = new MessageDialog("Settings info", "Info");
			dialog.Commands.Add(new UICommand("Copy info to clipboard", a =>
			{
				var dataPackage = new DataPackage();
				dataPackage.SetText(serializedSettings);
				Clipboard.SetContent(dataPackage);
			}));
			dialog.Commands.Add(new UICommand("Close"));
			await dialog.ShowAsync();
		}

		private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			var item = e.AddedItems[0] as PivotItem;
			if (item != null)
			{
				var headerText = item.Header as string;
				if (!string.IsNullOrWhiteSpace(headerText) && headerText.Equals("Change History", StringComparison.Ordinal))
				{
					_settings.MarkUpdateInfoRead();
				}
			}
		}


	}
}
