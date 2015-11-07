﻿using Autofac;
using Latest_Chatty_8.Common;
using Latest_Chatty_8.Networking;
using Latest_Chatty_8.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Navigation;

namespace Latest_Chatty_8.Views
{
	public sealed partial class TagView : ShellView
	{
		private SeenPostsManager seenPostsManager;
		private AuthenticationManager authManager;
		private LatestChattySettings settings;
		private ThreadMarkManager markManager;

		public override string ViewTitle
		{
			get { return "Tags"; }
		}

		public TagView()
		{
			this.InitializeComponent();
		}

		async protected override void OnNavigatedTo(NavigationEventArgs e)
		{
			base.OnNavigatedTo(e);
			var container = e.Parameter as Autofac.IContainer;
			this.seenPostsManager = container.Resolve<SeenPostsManager>();
			this.authManager = container.Resolve<AuthenticationManager>();
			this.settings = container.Resolve<LatestChattySettings>();
			this.markManager = container.Resolve<ThreadMarkManager>();
			this.singleThreadControl.Initialize(container);
			var commentThread = await JSONDownloader.Download(Networking.Locations.GetThread + "?id=" + "34139993");
			var parsedThread = (await CommentDownloader.ParseThread(commentThread["threads"][0], 0, this.seenPostsManager, this.authManager, this.settings, this.markManager));
			parsedThread.RecalculateDepthIndicators();
			this.singleThreadControl.DataContext = parsedThread;
		}
	}
}