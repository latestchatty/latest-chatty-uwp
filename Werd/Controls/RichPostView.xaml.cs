﻿using Autofac;
using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Werd.Common;
using Werd.DataModel;
using Werd.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Werd.Controls
{
	public sealed partial class RichPostView : UserControl
	{
		public event EventHandler<LinkClickedEventArgs> LinkClicked;
		public event EventHandler<ShellMessageEventArgs> ShellMessage;

		private readonly Tuple<string, string>[] _previewReplacements =
		{
			new Tuple<string, string>("r{", "<span class=\"jt_red\">"),
			new Tuple<string, string>("}r", "</span>"),
			new Tuple<string, string>("g{", "<span class=\"jt_green\">"),
			new Tuple<string, string>("}g", "</span>"),
			new Tuple<string, string>("b{", "<span class=\"jt_blue\">"),
			new Tuple<string, string>("}b", "</span>"),
			new Tuple<string, string>("y{", "<span class=\"jt_yellow\">"),
			new Tuple<string, string>("}y", "</span>"),
			new Tuple<string, string>("e[", "<span class=\"jt_olive\">"),
			new Tuple<string, string>("]e", "</span>"),
			new Tuple<string, string>("l[", "<span class=\"jt_lime\">"),
			new Tuple<string, string>("]l", "</span>"),
			new Tuple<string, string>("n[", "<span class=\"jt_orange\">"),
			new Tuple<string, string>("]n", "</span>"),
			new Tuple<string, string>("p[", "<span class=\"jt_pink\">"),
			new Tuple<string, string>("]p", "</span>"),
			new Tuple<string, string>("/[", "<i>"),
			new Tuple<string, string>("]/", "</i>"),
			new Tuple<string, string>("b[", "<b>"),
			new Tuple<string, string>("]b", "</b>"),
			new Tuple<string, string>("q[", "<span class=\"jt_quote\">"),
			new Tuple<string, string>("]q", "</span>"),
			new Tuple<string, string>("s[", "<span class=\"jt_sample\">"),
			new Tuple<string, string>("]s", "</span>"),
			new Tuple<string, string>("_[", "<u>"),
			new Tuple<string, string>("]_", "</u>"),
			new Tuple<string, string>("-[", "<span class=\"jt_strike\">"),
			new Tuple<string, string>("]-", "</span>"),
			new Tuple<string, string>("o[", "<span class=\"jt_spoiler\">"),
			new Tuple<string, string>("]o", "</span>"),
			new Tuple<string, string>("/{{", "<pre class=\"jt_code\">"),
			new Tuple<string, string>("}}/", "</pre>")
		};

		private string _loadedText;
		private int _loadedPostId;
		private readonly List<TypedEventHandler<Hyperlink, HyperlinkClickEventArgs>> _hyperlinkClicks = new List<TypedEventHandler<Hyperlink, HyperlinkClickEventArgs>>();

		public RichPostView()
		{
			InitializeComponent();
		}

		private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(nameof(AppGlobal.Settings.LoadImagesInline), StringComparison.Ordinal))
			{
				LoadPost(_loadedText, AppGlobal.Settings.LoadImagesInline);
			}
		}

		#region Public Methods

		public void LoadPostPreview(string v)
		{
			foreach (var replacement in _previewReplacements)
			{
				v = v.Replace(replacement.Item1, replacement.Item2, StringComparison.Ordinal);
			}
			LoadPost(v, false);
		}

		public void LoadPost(string v, bool embedImages)
		{
			PopulateBox(v, embedImages);
		}
		#endregion

		private class TagFind
		{
			public string TagName { get; private set; }
			public RunType Type { get; private set; }

			public TagFind(string tagName, RunType type)
			{
				TagName = tagName;
				Type = type;
			}
		}

		private readonly TagFind[] _findTags =
		{
			new TagFind("red", RunType.Red),
			new TagFind("green", RunType.Green),
			new TagFind("blue", RunType.Blue),
			new TagFind("yellow", RunType.Yellow),
			new TagFind("olive", RunType.Olive),
			new TagFind("lime", RunType.Lime),
			new TagFind("orange", RunType.Orange),
			new TagFind("pink", RunType.Pink),
			new TagFind("quote", RunType.Quote),
			new TagFind("sample", RunType.Sample),
			new TagFind("strike", RunType.Strike),
			new TagFind("spoiler", RunType.Spoiler)
		};

		private readonly string[] _endTags =
		{
			"</u>",
			"</i>",
			"</b>",
			"</span>",
			"</pre>"
		};

		private readonly List<List<Inline>> _spoilers = new List<List<Inline>>();

		private void PopulateBox(string body, bool embedImages)
		{
			UnbindHyperlinks();
			if (body == null) return;
			_spoilers.Clear();
			_loadedText = body;
			PostBody.Blocks.Clear();

			try
			{
				PostBody.Blocks.Add(CreateBodyParagraph(body, embedImages));
			}
			catch (Exception ex)
			{
				var para = new Paragraph();
				para.Inlines.Add(CreateNewRun(new List<RunType> { RunType.Red, RunType.Bold }, "Error parsing post. Here's some more info:" + Environment.NewLine));
				para.Inlines.Add(CreateNewRun(new List<RunType> { RunType.Red }, ex.Message + Environment.NewLine));
				var stackPara = new Paragraph();
				stackPara.Inlines.Add(CreateNewRun(new List<RunType>(), ex.StackTrace));
				var spoiler = new Spoiler();
				spoiler.SetText(stackPara);
				var inlineControl = new InlineUIContainer
				{
					Child = spoiler
				};
				para.Inlines.Add(inlineControl);
				PostBody.Blocks.Add(para);
			}
		}

		private Paragraph CreateBodyParagraph(
			string bodyText,
			bool embedImages)
		{
			var para = new Paragraph();
			var appliedRunTypes = new Stack<RunType>();
			List<Inline> spoilerContainer = null;
			var nestedSpoilerCount = 0;
			var builder = new StringBuilder();
			var iCurrentPosition = 0;

			while (iCurrentPosition < bodyText.Length)
			{
				var (type, lengthOfTag) = FindRunTypeAtPosition(bodyText, iCurrentPosition);
				var positionIncrement = lengthOfTag;
				switch (type)
				{
					case RunType.Hyperlink:
						//Handle special.

						//Complete any current run.
						AddSegment(para, appliedRunTypes, builder, spoilerContainer);

						//Find the closing tag.
						var closeLocation = bodyText.IndexOf("</a>", iCurrentPosition + lengthOfTag, StringComparison.Ordinal);
						if (closeLocation > -1)
						{
							var startOfHref = bodyText.IndexOf("href=\"", iCurrentPosition, StringComparison.Ordinal);
							if (startOfHref > -1)
							{
								startOfHref += 6;
								var endOfHref = bodyText.IndexOf("\"", startOfHref, StringComparison.Ordinal);
								var linkText = bodyText.Substring(iCurrentPosition + lengthOfTag, closeLocation - (iCurrentPosition + lengthOfTag));
								var link = bodyText.Substring(startOfHref, endOfHref - startOfHref);
								InlineUIContainer imageContainer = null;
								Hyperlink hyperLink = new Hyperlink();
								var run = CreateNewRun(appliedRunTypes, link);
								hyperLink.Foreground = new SolidColorBrush((Color)App.Current.Resources["SystemAccentColor"]); // new SolidColorBrush(Color.FromArgb(255, 174, 174, 155));
								hyperLink.Inlines.Add(run);
								hyperLink.Click += HyperLink_Click;

								var linkIsImage = EmbedHelper.GetEmbedType(new Uri(link)) == EmbedTypes.Image;
								if (embedImages && linkIsImage)
								{
									var image = new Image
									{
										Source = new BitmapImage(new Uri(link)),
										MinWidth = 20,
										MinHeight = 20,
										MaxWidth = 400,
										MaxHeight = 400
									};
									imageContainer = new InlineUIContainer() { Child = image };
								}
								var copyLink = new Hyperlink
								{
									Foreground = new SolidColorBrush((Color)App.Current.Resources["SystemAccentColorDark3"]),
									UnderlineStyle = UnderlineStyle.None
								};
								var copyRun = CreateNewRun(appliedRunTypes, " ");
								copyRun.FontFamily = new FontFamily("Segoe MDL2 Assets");
								copyLink.Inlines.Add(copyRun);
								void copyLinkClicked(Hyperlink a, HyperlinkClickEventArgs b)
								{
									var dataPackage = new DataPackage();
									dataPackage.SetText(link);
									Clipboard.SetContent(dataPackage);
									ShellMessage?.Invoke(this, new ShellMessageEventArgs("Link copied to clipboard."));
								}
								copyLink.Click += copyLinkClicked;
								_hyperlinkClicks.Add(copyLinkClicked);

								ToolTipService.SetToolTip(copyLink, new ToolTip { Content = "Copy link to clipboard" });

								Hyperlink openExternal = null;

								if (AppGlobal.Settings.OpenUnknownLinksInEmbeddedBrowser)
								{
									openExternal = new Hyperlink
									{
										Foreground = new SolidColorBrush((Color)App.Current.Resources["SystemAccentColorDark3"]),
										UnderlineStyle = UnderlineStyle.None
									};
									var openExternalRun = CreateNewRun(appliedRunTypes, " ");
									openExternalRun.FontFamily = new FontFamily("Segoe MDL2 Assets");
									openExternal.Inlines.Add(openExternalRun);
									async void launchLinkClicked(Hyperlink _, HyperlinkClickEventArgs __)
									{
										await Launcher.LaunchUriAsync(new Uri(link));
									};
									openExternal.Click += launchLinkClicked;
									_hyperlinkClicks.Add(launchLinkClicked);

									ToolTipService.SetToolTip(openExternal, new ToolTip { Content = "Open link in external browser" });
								}

								Hyperlink inlineImageToggle = null;

								if (linkIsImage)
								{
									inlineImageToggle = new Hyperlink
									{
										Foreground = new SolidColorBrush((Color)App.Current.Resources["SystemAccentColorDark3"]),
										UnderlineStyle = UnderlineStyle.None
									};
									var inlineImageToggleRun = CreateNewRun(appliedRunTypes, embedImages ? " " : " ");
									inlineImageToggleRun.FontFamily = new FontFamily("Segoe MDL2 Assets");
									inlineImageToggle.Inlines.Add(inlineImageToggleRun);
									void toggleEmbeddedImagesClicked(Hyperlink _, HyperlinkClickEventArgs __)
									{
										LoadPost(_loadedText, !embedImages);
									}
									inlineImageToggle.Click += toggleEmbeddedImagesClicked;
									_hyperlinkClicks.Add(toggleEmbeddedImagesClicked);
									ToolTipService.SetToolTip(inlineImageToggle, new ToolTip() { Content = embedImages ? "Hide all inline images" : "Show all images inline" });
								}

								if (!linkText.Equals(link, StringComparison.Ordinal))
								{
									var r = CreateNewRun(appliedRunTypes, "(" + linkText + ") - ");
									if (spoilerContainer != null)
									{
										spoilerContainer.Add(r);
									}
									else
									{
										para.Inlines.Add(r);
									}
								}
								if (spoilerContainer != null)
								{
									spoilerContainer.Add(hyperLink);
									spoilerContainer.Add(copyLink);
									if (openExternal != null) spoilerContainer.Add(openExternal);
									if (inlineImageToggle != null) spoilerContainer.Add(inlineImageToggle);
									spoilerContainer.Add(new Run() { Text = " " });
									if (imageContainer != null)
									{
										spoilerContainer.Add(new LineBreak());
										spoilerContainer.Add(imageContainer);
										spoilerContainer.Add(new LineBreak());
									}
								}
								else
								{
									para.Inlines.Add(hyperLink);
									para.Inlines.Add(copyLink);
									if (openExternal != null) para.Inlines.Add(openExternal);
									if (inlineImageToggle != null) para.Inlines.Add(inlineImageToggle);
									para.Inlines.Add(new Run() { Text = " " });
									if (imageContainer != null)
									{
										para.Inlines.Add(new LineBreak());
										para.Inlines.Add(imageContainer);
										para.Inlines.Add(new LineBreak());
									}
								}
								positionIncrement = closeLocation + 4 - iCurrentPosition;
							}
						}
						break;
					case RunType.None:
						builder.Append(bodyText[iCurrentPosition]);
						break;
					default:
						AddSegment(para, appliedRunTypes, builder, spoilerContainer);

						if (type == RunType.Spoiler)
						{
							spoilerContainer = spoilerContainer ?? new List<Inline>();
							if (spoilerContainer != null)
							{
								nestedSpoilerCount++;
							}
							else
							{
								spoilerContainer = new List<Inline>();
							}
						}

						if (type != RunType.End)
						{
							appliedRunTypes.Push(type);
						}

						if (type == RunType.End)
						{
							var appliedType = appliedRunTypes.Pop();
							if (appliedType == RunType.Spoiler && --nestedSpoilerCount == 0)
							{
								if (spoilerContainer != null)
								{
									var spoiler = new Hyperlink();
									_spoilers.Add(spoilerContainer);
									var spoilerIndex = _spoilers.Count - 1;
									void spoilerClicked(Hyperlink _, HyperlinkClickEventArgs __)
									{
										var hyperlinkIndex = para.Inlines.IndexOf(spoiler);
										para.Inlines.Remove(spoiler);
										foreach (var sp in _spoilers[spoilerIndex])
										{
											para.Inlines.Insert(hyperlinkIndex, sp);
											hyperlinkIndex++;
										}
									}
									spoiler.Click += spoilerClicked;
									_hyperlinkClicks.Add(spoilerClicked);
									//Warning icon
									spoiler.Inlines.Add(new Run() { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets") });
									spoiler.Inlines.Add(new Run() { Text = " Show Spoiler " });
									//Warning icon
									spoiler.Inlines.Add(new Run() { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets") });
									para.Inlines.Add(spoiler);
									spoilerContainer = null;
								}
							}
						}
						break;
				}
				iCurrentPosition += positionIncrement;
			}
			AddSegment(para, appliedRunTypes, builder, spoilerContainer);
			return para;
		}

		private void AddSegment(Paragraph para, Stack<RunType> appliedRunTypes, StringBuilder builder, List<Inline> spoilerContainer)
		{
			if (builder.Length == 0) return;

			var run = CreateNewRun(appliedRunTypes, builder.ToString());

			if (!string.IsNullOrEmpty(run.Text))
			{
				if (spoilerContainer != null)
				{
					spoilerContainer.Add(run);
				}
				else
				{
					para.Inlines.Add(run);
				}
			}

			builder.Clear();
		}

		private Run CreateNewRun(IEnumerable<RunType> appliedRunTypes, string text)
		{
			var run = new Run
			{
				FontSize = (double)Application.Current.Resources["ControlContentThemeFontSize"],
				Text = text
			};
			run.ApplyTypesToRun(appliedRunTypes.Reverse().ToList());
			return run;
		}

		private void HyperLink_Click(Hyperlink sender, HyperlinkClickEventArgs args)
		{
			var linkText = ((Run)sender.Inlines[0]).Text;
			DebugLog.AddMessage($"Clicked link {linkText}").ConfigureAwait(true).GetAwaiter().GetResult();
			LinkClicked?.Invoke(this, new LinkClickedEventArgs(new Uri(linkText), Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)));
		}

		private (RunType TagType, int TagLength) FindRunTypeAtPosition(string line, int position)
		{
			//Possible tag
			if (line[position] == '<')
			{
				if (position + 1 < line.Length)
				{
					if (line[position + 1] != '/')
					{
						if (line.IndexOf("<u>", position, StringComparison.Ordinal) == position)
						{
							return (RunType.Underline, 3);
						}
						if (line.IndexOf("<i>", position, StringComparison.Ordinal) == position)
						{
							return (RunType.Italics, 3);
						}
						if (line.IndexOf("<b>", position, StringComparison.Ordinal) == position)
						{
							return (RunType.Bold, 3);
						}
						//It's a style tag
						if (line.IndexOf("<span class=\"jt_", position, StringComparison.Ordinal) == position)
						{
							foreach (var tagToFind in _findTags)
							{
								if (line.IndexOf(tagToFind.TagName, position + 16, StringComparison.Ordinal) == position + 16)
								{
									return (tagToFind.Type, line.IndexOf('>', position + 16) + 1 - position);
								}
							}
							//There's apparently a WTF242 style, not going to handle that.  Maybe they'll add more later, don't want to break if it's there.
							return (RunType.UnknownStyle, line.IndexOf('>', position + 16) + 1 - position);
						}
						if (line.IndexOf("<a ", position, StringComparison.Ordinal) == position)
						{
							var tagEnd = line.IndexOf('>', position);
							if (line.Substring(position, tagEnd - position).Contains("href=\"", StringComparison.Ordinal))
							{
								return (RunType.Hyperlink, tagEnd - position + 1);
							}
						}
						if (line.IndexOf("<pre class=\"jt_code\">", position, StringComparison.Ordinal) == position)
						{
							return (RunType.Code, 21);
						}
					}

					foreach (var tag in _endTags)
					{
						if (line.IndexOf(tag, position, StringComparison.Ordinal) == position)
						{
							return (RunType.End, tag.Length);
						}
					}
				}
			}

			return (RunType.None, 1);
		}

		private void DataContextUpdated(FrameworkElement _, DataContextChangedEventArgs args)
		{
			var comment = args.NewValue as Comment;
			if (comment == null) return;
			if (comment.Id == _loadedPostId)
			{
				DebugLog.AddMessage($"Post id {comment.Id} already loaded. Skipping.").ConfigureAwait(true).GetAwaiter().GetResult();
				return;
			}
			_loadedPostId = comment.Id;
			DebugLog.AddMessage($"Loading post id {comment.Id}").ConfigureAwait(true).GetAwaiter().GetResult();
			LoadPost(comment.Body, AppGlobal.Settings.LoadImagesInline);
		}

		private void PostBodyControlLoaded(object sender, RoutedEventArgs e)
		{
			PostBody.SelectionFlyout.Opening += PostbodySelectionFlyoutOpening;
			PostBody.ContextFlyout.Opening += PostbodySelectionFlyoutOpening;
			//Reload the post because the control may get unloaded and then hyperlinks will be unbound.
			LoadPost(_loadedText, AppGlobal.Settings.LoadImagesInline);
		}
		private void PostBodyControlUnloaded(object sender, RoutedEventArgs e)
		{
			PostBody.SelectionFlyout.Opening -= PostbodySelectionFlyoutOpening;
			PostBody.ContextFlyout.Opening -= PostbodySelectionFlyoutOpening;
		}

		private void PostbodySelectionFlyoutOpening(object sender, object e)
		{
			if (string.IsNullOrWhiteSpace(PostBody.SelectedText)) return;

			var flyout = (sender as CommandBarFlyout);
			if (!(flyout.Target == PostBody)) return;
			var cmd = new StandardUICommand(StandardUICommandKind.Open)
			{
				IconSource = new SymbolIconSource() { Symbol = Symbol.World },
				Description = "Search the web"
			};
			cmd.ExecuteRequested += async (_, __) => await Launcher.LaunchUriAsync(await UriHelper.MakeWebViewSafeUriOrSearch(PostBody.SelectedText).ConfigureAwait(true));

			flyout.PrimaryCommands.Add(new AppBarButton { Command = cmd });

			cmd = new StandardUICommand(StandardUICommandKind.Open)
			{
				IconSource = new SymbolIconSource { Symbol = Symbol.Find },
				Description = "Search Shacknews"
			};

			cmd.ExecuteRequested += (_, __) =>
			{
				if (Window.Current.Content is Shell f)
				{
					f.NavigateToPage(
						typeof(CustomSearchWebView),
						new Views.NavigationArgs.WebViewNavigationArgs
							(AppGlobal.Container,
							new Uri($"https://www.shacknews.com/search?chatty=1&type=4&chatty_term={Uri.EscapeUriString(PostBody.SelectedText)}&chatty_user=&chatty_author=&chatty_filter=all&result_sort=postdate_desc")
							)
					);
				}
			};
			flyout.PrimaryCommands.Add(new AppBarButton { Command = cmd });
		}

		private void ControlLoaded(object sender, RoutedEventArgs e)
		{
			DebugLog.AddMessage("RichPostViewLoaded").ConfigureAwait(true).GetAwaiter().GetResult();
			AppGlobal.Settings.PropertyChanged += Settings_PropertyChanged;
		}

		private void ControlUnloaded(object sender, RoutedEventArgs e)
		{
			DebugLog.AddMessage("RichPostViewUnloaded").ConfigureAwait(true).GetAwaiter().GetResult();
			AppGlobal.Settings.PropertyChanged -= Settings_PropertyChanged;
			UnbindHyperlinks();
		}

		private void UnbindHyperlinks()
		{
			foreach (var block in PostBody.Blocks.Select(b => b as Paragraph).Where(b => b != null))
			{
				foreach (var inline in block.Inlines)
				{
					var link = inline as Hyperlink;
					if (link != null)
					{
						link.Click -= HyperLink_Click;
						foreach (var item in _hyperlinkClicks)
						{
							link.Click -= item;
						}
					}
				}
			}
			_hyperlinkClicks.Clear();
		}
	}
}
