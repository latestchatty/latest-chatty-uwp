﻿using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using Common;
using Latest_Chatty_8.Common;

namespace Latest_Chatty_8.DataModel
{
	public class CommentThread : BindableBase
	{
		private readonly ObservableCollection<Comment> _comments;

		#region Properties
		private ReadOnlyObservableCollection<Comment> _commentsRo;
		public ReadOnlyObservableCollection<Comment> Comments { get => _commentsRo;
			private set => SetProperty(ref _commentsRo, value);
		}

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

		public bool IsExpired => (Comments[0].Date.AddHours(18).ToUniversalTime() < DateTime.UtcNow);

		public bool NewlyAdded { get; set; }

		private bool npcViewedNewlyAdded;
		public bool ViewedNewlyAdded
		{
			get => npcViewedNewlyAdded;
			set => SetProperty(ref npcViewedNewlyAdded, value);
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

			Invisible = invisible;
			Id = rootComment.Id;
			if (rootComment.AuthorType == AuthorType.Self) { UserParticipated = true; }
			HasNewReplies = rootComment.IsNew;
			NewlyAdded = newlyAdded;
			ViewedNewlyAdded = !newlyAdded;
			_comments.Add(rootComment);
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

		public void AddReply(Comment c, bool recalculateDepth = true)
		{
			//Can't directly add a parent comment.
			if (c.ParentId == 0) return;

			Comment insertAfter;
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
				var location = _comments.IndexOf(insertAfter);
				if (Comments.First().Author == c.Author && c.AuthorType != AuthorType.Self)
				{
					c.AuthorType = AuthorType.ThreadOp;
				}
				_comments.Insert(location + 1, c);
				if (c.AuthorType == AuthorType.Self)
				{
					UserParticipated = true;
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
		public void RecalculateDepthIndicators()
		{
			//var orderedById = _comments.OrderBy(c => c.Id);
			foreach (var c in _comments)
			{
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
