﻿using Common;
using Microsoft.Toolkit.Collections;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Werd.Common;

namespace Werd.DataModel
{
	public class CommentThread : BindableBase
	{
		private readonly ObservableCollection<Comment> _comments;

		#region Properties
		private ReadOnlyObservableCollection<Comment> _commentsRo;
		public ReadOnlyObservableCollection<Comment> Comments
		{
			get => _commentsRo;
			private set => SetProperty(ref _commentsRo, value);
		}

		public ObservableGroup<CommentThread, Comment> CommentsGroup { get; }

		private int npcId;
		/// <summary>
		/// Comment ID
		/// </summary>
		[DataMember]
		public int Id
		{
			get => npcId;
			set => SetProperty(ref npcId, value);
		}

		private bool npcUserParticipated;
		/// <summary>
		/// Indicates whether the currently logged in user has participated in this thread or not
		/// </summary>
		[DataMember]
		public bool UserParticipated
		{
			get => npcUserParticipated;
			set => SetProperty(ref npcUserParticipated, value);
		}

		private bool npcHasNewReplies;
		/// <summary>
		/// Indicates if this comment has new replies since the last time it was loaded
		/// </summary>
		[DataMember]
		public bool HasNewReplies
		{
			get => npcHasNewReplies;
			set => SetProperty(ref npcHasNewReplies, value);
		}

		private bool npcHasRepliesToUser;
		[DataMember]
		public bool HasRepliesToUser
		{
			get => npcHasRepliesToUser;
			set => SetProperty(ref npcHasRepliesToUser, value);
		}

		private bool npcHasNewRepliesToUser;
		[DataMember]
		public bool HasNewRepliesToUser
		{
			get => npcHasNewRepliesToUser;
			set => SetProperty(ref npcHasNewRepliesToUser, value);
		}

		//TODO: This is redundant data that could be handled by different collections in the chatty manager.
		private bool npcIsPinned;
		/// <summary>
		/// Indicates if this comment is pinned or not
		/// </summary>
		[DataMember]
		public bool IsPinned
		{
			get => npcIsPinned;
			set => SetProperty(ref npcIsPinned, value);
		}

		private bool npcIsCollapsed;
		/// <summary>
		/// Indicates if this comment is collapsed or not
		/// </summary>
		[DataMember]
		public bool IsCollapsed
		{
			get => npcIsCollapsed;
			set => SetProperty(ref npcIsCollapsed, value);
		}

		private bool npcTruncateThread;
		/// <summary>
		/// Indicates if this thread should be truncated due to reply count
		/// </summary>
		[DataMember]
		public bool TruncateThread
		{
			get => npcTruncateThread;
			set
			{
				//WARNING - Thread safe?? Should this be in chatty manager?
				if (!value)
				{
					CommentsGroup.Clear();
					_comments.Skip(1).ToList().ForEach(c => CommentsGroup.Add(c));
				}
				else
				{
					SetTruncatedCommentsLastX();
					//If re-truncating, collapse all threads, otherwise leave 'em alone.
					foreach (var item in CommentsGroup)
					{
						item.IsSelected = false;
					}
				}
				//Don't actually set the thread truncated if we're not above the threshold. Basically just resetting items to not selected.
				if (_comments.Count <= AppGlobal.Settings.TruncateLimit) return;
				SetProperty(ref npcTruncateThread, value);
			}
		}

		public bool IsExpired => (Comments[0].Date.AddHours(18).ToUniversalTime() < DateTime.UtcNow);

		public bool NewlyAdded { get; set; }

		private bool npcViewedNewlyAdded;
		public bool ViewedNewlyAdded
		{
			get => npcViewedNewlyAdded;
			set => SetProperty(ref npcViewedNewlyAdded, value);
		}

		private bool npcCanTruncate;
		public bool CanTruncate
		{
			get => npcCanTruncate;
			set => SetProperty(ref npcCanTruncate, value);
		}

		//TODO: This is redundant data that could be handled by different collections in the chatty manager.
		/// <summary>
		/// Indicates if this thread should ever be shown in the active chatty.
		/// When set to true, it will not be shown in the active chatty. It will also never be removed when old threads are cleaned up.
		/// </summary>
		public bool Invisible { get; set; }
		#endregion

		#region Ctor
		public CommentThread(Comment rootComment, bool newlyAdded = false, bool invisible = false)
		{
			_comments = new ObservableCollection<Comment>();
			Comments = new ReadOnlyObservableCollection<Comment>(_comments);
			CommentsGroup = new ObservableGroup<CommentThread, Comment>(this);

			rootComment.Thread = this;
			Invisible = invisible;
			Id = rootComment.Id;
			if (rootComment.AuthorType == AuthorType.Self) { UserParticipated = true; }
			HasNewReplies = rootComment.IsNew;
			NewlyAdded = newlyAdded;
			ViewedNewlyAdded = !newlyAdded;
			rootComment.IsSelected = true;
			_comments.Add(rootComment);
			AppGlobal.Settings.PropertyChanged += Settings_PropertyChanged;
		}

		private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(AppGlobal.Settings.UseMainDetail):
					if (AppGlobal.Settings.UseMainDetail)
					{
						CanTruncate = false;
					}
					else
					{
						CanTruncate = _comments.Count > 2;
					}
					break;
				case nameof(AppGlobal.Settings.TruncateLimit):
					TruncateThread = Comments.Count > AppGlobal.Settings.TruncateLimit;
					if (TruncateThread) { SetTruncatedCommentsLastX(); }
					break;
				case nameof(AppGlobal.Settings.UseCompactLayout):
				case nameof(AppGlobal.Settings.FontSize):
					RecalculateDepthIndicators();
					break;
			}
		}
		#endregion

		#region Public Methods
		/// <summary>
		/// This is pretty shady but it will send a propertychanged event for the Date property causing bindings to be updated.
		/// </summary>
		public void ForceDateRefresh()
		{
			// ReSharper disable once ExplicitCallerInfoArgument
			OnPropertyChanged("Date");
		}

		public async Task AddReply(Comment c, bool recalculateDepth = true)
		{
			c.Thread = this;
			//Can't directly add a parent comment.
			if (c.ParentId == 0) return;
			var insertLocation = -1;

			Comment insertAfter;
			if (c.AuthorType == AuthorType.Self)
			{
				ResyncGrouped(); //Add all replies so we can calculate where the new post needs to go once.
			}
			var repliesToParent = _comments.Where(c1 => c1.ParentId == c.ParentId).ToList();
			if (repliesToParent.Any())
			{
				//If there are replies, we need to figure out where we fit in.
				var lastReplyBeforeUs = repliesToParent.OrderBy(r => r.Id).LastOrDefault(r => r.Id < c.Id);  //Find the last reply that should come before this one.
				if (lastReplyBeforeUs != null)
				{
					insertAfter = FindLastCommentInChain(lastReplyBeforeUs);    //Now we look at all the replies to this comment, if any.  Find the last one of those.  That's where we need to insert ourselves.
				}
				else
				{
					//If there are no comments that come before this one, we find the parent comment and insert ourselves right after it.
					insertAfter = _comments.SingleOrDefault(p => p.Id == c.ParentId);
				}
			}
			else
			{
				//If there aren't any replies to the parent of this post, we're the first one.  We'll just stick ourselves at the end.
				insertAfter = _comments.SingleOrDefault(p => p.Id == c.ParentId);
			}
			if (insertAfter != null)
			{
				insertLocation = _comments.IndexOf(insertAfter) + 1;
				await AppGlobal.DebugLog.AddMessage($"Inserting comment id {c.Id} at {insertLocation}").ConfigureAwait(true);
				if (Comments.First().Author == c.Author && c.AuthorType != AuthorType.Self)
				{
					c.AuthorType = AuthorType.ThreadOp;
				}
				_comments.Insert(insertLocation, c);
				if (c.AuthorType == AuthorType.Self)
				{
					UserParticipated = true;
					//If it was us that replied, we want to see it because we caused it to happen so we're expecting it.
					CommentsGroup.Insert(insertLocation - 1, c);
				}
				//If we already have replies to the user, we don't have to update this.  Posts can get nuked but that happens elsewhere.
				if (!HasRepliesToUser)
				{
					HasRepliesToUser = Comments.Any(c1 => Comments.Any(c2 => c2.Id == c1.ParentId && c2.AuthorType == AuthorType.Self));
				}
				if (!HasNewRepliesToUser)
				{
					HasNewRepliesToUser = Comments.Any(c1 => c1.IsNew && Comments.Any(c2 => c2.Id == c1.ParentId && c2.AuthorType == AuthorType.Self));
				}
			}
			HasNewReplies = _comments.Any(c1 => c1.IsNew);
			if (recalculateDepth)
			{
				RecalculateDepthIndicators();
			}
		}

		public void ChangeCommentCategory(int commentId, PostCategory newCategory)
		{
			var comment = _comments.First(c => c.Id == commentId);
			if (newCategory == PostCategory.nuked)
			{
				try
				{
					RemoveAllChildComments(comment);
				}
				//It's hard to test nuked posts (yeah, yeah, unit testing...) so we'll just ignore it if it fails in "production", otherwise if there's a debugger attached we'll check it out.
				catch (Exception)
				{ Debugger.Break(); }
			}
			else
			{
				comment.Category = newCategory;
			}
		}

		public void ResyncGrouped()
		{
			if (TruncateThread)
			{
				SetTruncatedCommentsLastX();
			}
			else
			{
				CommentsGroup.Clear();
				foreach (var comment in _comments.Skip(1))
				{
					CommentsGroup.Add(comment);
				}
			}
			CanTruncate = !AppGlobal.Settings.UseMainDetail && _comments.Count > 1;// && _comments.Count > Global.Settings.TruncateLimit;
		}
		public void RecalculateDepthIndicators()
		{
			var sortedComments = _comments.OrderByDescending(c => c.Id);
			byte color = 255;
			foreach (var c in sortedComments)
			{
				c.PreviewColor = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, color, color, color));
				color = (byte)Math.Max(155, color - 10);

				var indicators = new char[c.Depth];
				for (var depth = 0; depth < c.Depth; depth++)
				{
					//Figure out if we're the last at our depth.
					if (depth == c.Depth - 1)
					{

						indicators[depth] = IsLastCommentAtDepth(c) ? TreeImageRepo.End : TreeImageRepo.Junction;
					}
					else
					{
						var parentForDepth = FindParentAtDepth(c, depth + 1);
						if (!IsLastCommentAtDepth(parentForDepth))
						{
							indicators[depth] = TreeImageRepo.Passthrough;
						}
						else
						{
							indicators[depth] = TreeImageRepo.Empty;
						}
					}
				}
				c.DepthImage = TreeImageRepo.FetchTreeImage(indicators);
			}
		}

		#endregion

		#region Private Helpers
		private void SetTruncatedCommentsLatestX()
		{
			var commentsToAddOrKeep = _comments.OrderBy(x => x.Id).Skip(_comments.Count - AppGlobal.Settings.TruncateLimit).ToList();
			var commentsToRemove = CommentsGroup.Except(commentsToAddOrKeep).ToList();
			foreach (var commentToRemove in commentsToRemove)
			{
				CommentsGroup.Remove(commentToRemove);
			}

			foreach (var commentToAdd in commentsToAddOrKeep)
			{
				commentToAdd.IsSelected = false;
				var insertLocation = -1;
				for (int i = 0; i < CommentsGroup.Count; i++)
				{
					if (commentToAdd.Id < CommentsGroup[i].Id)
					{
						insertLocation = i;
						break;
					}
				}
				if (insertLocation == -1) insertLocation = CommentsGroup.Count;
				if (!CommentsGroup.Contains(commentToAdd)) CommentsGroup.Insert(insertLocation, commentToAdd);
			}
		}

		private void SetTruncatedCommentsLastX()
		{
			var commentsToKeep = _comments.Skip(_comments.Count - AppGlobal.Settings.TruncateLimit).Except(new[] { _comments.First() }).ToList();
			var commentsToRemove = CommentsGroup.Except(commentsToKeep).ToList();
			foreach (var commentToRemove in commentsToRemove)
			{
				CommentsGroup.Remove(commentToRemove);
			}

			foreach (var comment in commentsToKeep)
			{
				if (!CommentsGroup.Contains(comment)) CommentsGroup.Add(comment);
			}

			for (int i = 0; i < commentsToKeep.Count; i++)
			{
				var currentIndex = CommentsGroup.IndexOf(commentsToKeep[i]);
				var desiredIndex = commentsToKeep.IndexOf(commentsToKeep[i]);
				if (currentIndex != desiredIndex)
				{
					CommentsGroup.Move(currentIndex, desiredIndex);
				}
			}
		}

		private Comment FindParentAtDepth(Comment c, int depth)
		{
			var parent = _comments.Single(c1 => c1.Id == c.ParentId);
			if (parent.Depth == depth)
			{
				return parent;
			}
			return FindParentAtDepth(parent, depth);
		}

		private bool IsLastCommentAtDepth(Comment c)
		{
			var threadsAtDepth = _comments.Where(c1 => c1.ParentId == c.ParentId).OrderBy(c1 => c1.Id);
			return threadsAtDepth.Last().Id == c.Id;
		}

		private Comment FindLastCommentInChain(Comment c)
		{
			var childComments = _comments.Where(c1 => c1.ParentId == c.Id).ToList();
			if (childComments.Any())
			{
				var lastComment = childComments.OrderBy(c1 => c1.Id).LastOrDefault();
				return FindLastCommentInChain(lastComment);
			}

			return c;
		}

		private void RemoveAllChildComments(Comment start)
		{
			var commentsToRemove = _comments.Where(c => c.ParentId == start.Id).ToList();
			foreach (var child in commentsToRemove)
			{
				RemoveAllChildComments(child);
			}
			_comments.Remove(start);
		}
		#endregion
	}
}
