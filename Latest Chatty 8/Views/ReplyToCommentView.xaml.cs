﻿using Latest_Chatty_8.DataModel;
using Latest_Chatty_8.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Basic Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234237

namespace Latest_Chatty_8.Views
{
	/// <summary>
	/// A basic page that provides characteristics common to most applications.
	/// </summary>
	public sealed partial class ReplyToCommentView : Latest_Chatty_8.Common.LayoutAwarePage
	{
		private Comment replyToComment;

		public ReplyToCommentView()
		{
			this.InitializeComponent();
			Window.Current.SizeChanged += WindowSizeChanged;
		}

		private void WindowSizeChanged(object sender, Windows.UI.Core.WindowSizeChangedEventArgs e)
		{
			this.LayoutUI();
		}

		/// <summary>
		/// Populates the page with content passed during navigation.  Any saved state is also
		/// provided when recreating a page from a prior session.
		/// </summary>
		/// <param name="navigationParameter">The parameter value passed to
		/// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested.
		/// </param>
		/// <param name="pageState">A dictionary of state preserved by this page during an earlier
		/// session.  This will be null the first time a page is visited.</param>
		protected override void LoadState(Object navigationParameter, Dictionary<String, Object> pageState)
		{
			this.LayoutUI();
			this.replyToComment = navigationParameter as Comment;
			this.DefaultViewModel["ReplyToComment"] = this.replyToComment;
		}

		/// <summary>
		/// Preserves state associated with this page in case the application is suspended or the
		/// page is discarded from the navigation cache.  Values must conform to the serialization
		/// requirements of <see cref="SuspensionManager.SessionState"/>.
		/// </summary>
		/// <param name="pageState">An empty dictionary to be populated with serializable state.</param>
		protected override void SaveState(Dictionary<String, Object> pageState)
		{
		}

		async private void SendButtonClicked(object sender, RoutedEventArgs e)
		{
			var button = sender as Button;

			if (this.replyText.Text.Length <= 5)
			{
				var dlg = new Windows.UI.Popups.MessageDialog("Post something longer.");
				await dlg.ShowAsync();
				return;
			}

			this.backButton.Focus(Windows.UI.Xaml.FocusState.Programmatic);
			button.IsEnabled = false;
			
			this.progress.IsIndeterminate = true;
			this.progress.Visibility = Windows.UI.Xaml.Visibility.Visible;

			var content = this.replyText.Text;

			var encodedBody = Uri.EscapeUriString(content);
			content = "body=" + encodedBody;
			content += "&parent_id=" + this.replyToComment.Id;

			POSTHelper.Send(Locations.PostUrl, content, true);

			this.progress.IsIndeterminate = false;
			this.progress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
			button.IsEnabled = true;

			CoreServices.Instance.PostedAComment = true;
			this.Frame.GoBack();
		}

		private void LayoutUI()
		{
			if (Windows.UI.ViewManagement.ApplicationView.Value == Windows.UI.ViewManagement.ApplicationViewState.Snapped)
			{
				if (Window.Current.Bounds.Left == 0) //Snapped Left side.
				{
					this.postButton.HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Left;
					return;
				}
			}

			this.postButton.HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Right;
		}
	}
}
