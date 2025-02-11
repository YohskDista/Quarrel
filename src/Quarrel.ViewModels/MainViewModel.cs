﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiscordAPI.Models;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using JetBrains.Annotations;
using Quarrel.Messages.Gateway;
using Quarrel.Messages.Navigation;
using Quarrel.Messages.Posts.Requests;
using Quarrel.Models.Bindables;
using Quarrel.Services.Cache;
using Quarrel.Services.Gateway;
using Quarrel.Services.Guild;
using Quarrel.Services.Rest;
using Quarrel.Services.Users;
using Quarrel.Navigation;
using Quarrel.Services.Settings;
using Quarrel.ViewModels.Services.DispatcherHelper;
using System.Collections.Concurrent;
using Quarrel.ViewModels.Services;

namespace Quarrel.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ICacheService CacheService;
        private readonly ISettingsService SettingsService;
        private readonly IDiscordService DiscordService;
        public readonly ICurrentUsersService CurrentUsersService;
        private readonly IGatewayService GatewayService;
        private readonly IGuildsService GuildsService;
        private readonly ISubFrameNavigationService SubFrameNavigationService;
        private readonly IDispatcherHelper DispatcherHelper;

        public MainViewModel(ICacheService cacheService, ISettingsService settingsService, IDiscordService discordService, ICurrentUsersService currentUsersService, IGatewayService gatewayService, IGuildsService guildsService, ISubFrameNavigationService subFrameNavigationService, IDispatcherHelper dispatcherHelper)
        {
            CacheService = cacheService;
            SettingsService = settingsService;
            DiscordService = discordService;
            CurrentUsersService = currentUsersService;
            GatewayService = gatewayService;
            GuildsService = guildsService;
            SubFrameNavigationService = subFrameNavigationService;
            DispatcherHelper = dispatcherHelper;

            RegisterMessages();
        }

        private void RegisterMessages()
        {
            MessengerInstance.Register<GatewayInvalidSessionMessage>(this, async _ =>
            {
                await CacheService.Persistent.Roaming.DeleteValueAsync(Quarrel.Helpers.Constants.Cache.Keys.AccessToken);
                Login();
            });
            MessengerInstance.Register<GatewayReadyMessage>(this, async _ =>
            {
                DispatcherHelper.CheckBeginInvokeOnUi(() =>
                {
                    MessengerInstance.Send(new GuildNavigateMessage(GuildsService.Guilds["DM"]));
                });
            });
            MessengerInstance.Register<GuildNavigateMessage>(this, async m =>
            {
                if (Guild != m.Guild)
                {
                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        Channel = null;
                        Guild = m.Guild;
                        BindableMessages.Clear();
                        BindableMembers.Clear();
                        BindableChannels.ReplaceRange(m.Guild.Channels);
                    });
                }
            });
            MessengerInstance.Register<ChannelNavigateMessage>(this, async m =>
            {
                DispatcherHelper.CheckBeginInvokeOnUi(() =>
                {
                    Channel = m.Channel;
                });

                if (SettingsService.Roaming.GetValue<bool>(SettingKeys.FilterMembers))
                {
                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        BindableMembers.Clear();
                        BindableMembers.AddElementRange(CurrentUsersService.Users.Values.Where(user =>
                        {
                            if (Channel.IsTextChannel)
                            {
                                Permissions perms = new Permissions(Guild.Model.Roles
                                    .FirstOrDefault(x => x.Name == "@everyone").Permissions);
                                foreach (var role in user.Roles)
                                {
                                    perms.AddAllows((GuildPermission) role.Permissions);
                                }

                                GuildPermission roleDenies = 0;
                                GuildPermission roleAllows = 0;
                                GuildPermission memberDenies = 0;
                                GuildPermission memberAllows = 0;
                                foreach (Overwrite overwrite in (Channel.Model as GuildChannel).PermissionOverwrites)
                                    if (overwrite.Id == Channel.GuildId)
                                    {
                                        perms.AddDenies((GuildPermission) overwrite.Deny);
                                        perms.AddAllows((GuildPermission) overwrite.Allow);
                                    }
                                    else if (overwrite.Type == "role" && user.Model.Roles.Contains(overwrite.Id))
                                    {
                                        roleDenies |= (GuildPermission) overwrite.Deny;
                                        roleAllows |= (GuildPermission) overwrite.Allow;
                                    }
                                    else if (overwrite.Type == "member" && overwrite.Id == user.Model.User.Id)
                                    {
                                        memberDenies |= (GuildPermission) overwrite.Deny;
                                        memberAllows |= (GuildPermission) overwrite.Allow;
                                    }

                                perms.AddDenies(roleDenies);
                                perms.AddAllows(roleAllows);
                                perms.AddDenies(memberDenies);
                                perms.AddAllows(memberAllows);

                                // If owner add admin
                                if (Guild.Model.OwnerId == user.Model.User.Id)
                                {
                                    perms.AddAllows(GuildPermission.Administrator);
                                }

                                return perms.ReadMessages;
                            }

                            return true;
                        }));
                    });
                }

                await SemaphoreSlim.WaitAsync();
                try
                {
                    NewItemsLoading = true;
                    IEnumerable<Message> itemList = null;
                    //if (Channel.ReadState == null)
                    //    itemList = await ServicesManager.Discord.ChannelService.GetChannelMessages(m.Channel.Model.Id);
                    //else
                    //    itemList = await ServicesManager.Discord.ChannelService.GetChannelMessagesAround(m.Channel.Model.Id, Channel.ReadState.LastMessageId, 50);
                    try
                    {
                        itemList = await DiscordService.ChannelService.GetChannelMessages(m.Channel.Model.Id, 50);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                        return;
                    }

                    Message lastItem = null;

                    BindableMessage scrollItem = null;

                    List<BindableMessage> messages = new List<BindableMessage>();

                    foreach (Message item in itemList.Reverse())
                    {
                        messages.Add(new BindableMessage(item, guildId, lastItem, lastItem != null && m.Channel.ReadState != null && lastItem.Id == m.Channel.ReadState.LastMessageId));

                        if (lastItem != null && m.Channel.ReadState != null && lastItem.Id == m.Channel.ReadState.LastMessageId)
                        {
                            scrollItem = messages.LastOrDefault();
                        }

                        lastItem = item;
                    }

                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        BindableMessages.ReplaceRange(messages);
                        ScrollTo?.Invoke(this, scrollItem ?? BindableMessages.LastOrDefault());
                    });
                    NewItemsLoading = false;
                }
                finally
                {
                    SemaphoreSlim.Release();
                }
            });
            MessengerInstance.Register<GatewayMessageRecievedMessage>(this, async m =>
            {
                if (Channel != null && Channel.Model.Id == m.Message.ChannelId)
                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        if (GuildsService.CurrentChannels.TryGetValue(m.Message.ChannelId, out var currentChannel))
                        {
                            currentChannel.Typers.TryRemove(m.Message.User.Id, out var _);
                            BindableMessages.Add(new BindableMessage(m.Message, guildId, BindableMessages.LastOrDefault()?.Model));
                        }
                    });
            });
            MessengerInstance.Register<GatewayMessageDeletedMessage>(this, async m =>
            {
                if (Channel != null && Channel.Model.Id == m.ChannelId)
                {
                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        // LastOrDefault to start from the bottom
                        var msg = BindableMessages.LastOrDefault(x => x.Model.Id == m.MessageId);
                        if (msg != null)
                        {
                            BindableMessages.Remove(msg);
                        }
                    });
                }
            });
            MessengerInstance.Register<GatewayMessageUpdatedMessage>(this, async m =>
            {
                if (Channel != null && Channel.Model.Id == m.Message.ChannelId)
                {
                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        // LastOrDefault to start from the bottom
                        var msg = BindableMessages.LastOrDefault(x => x.Model.Id == m.Message.Id);
                        msg?.Update(m.Message);
                    });
                }
            });
            MessengerInstance.Register<GatewayReactionAddedMessage>(this, async m =>
            {
                var message = BindableMessages.FirstOrDefault(x => x.Model.Id == m.MessageId);
                if (message != null)
                {
                    if (message.Model.Reactions == null)
                    {
                        message.Model.Reactions = new List<Reactions>().AsEnumerable();
                    }
                    var reaction = message.Model.Reactions.FirstOrDefault(x => x.Emoji.Name == m.Emoji.Name && x.Emoji.Id == m.Emoji.Id);
                    if (reaction != null)
                    {
                        reaction.Count++;
                        // TODO: Find better update method
                        message.Model.Reactions = message.Model.Reactions.ToList().AsEnumerable();
                    }
                    else
                    {
                        var list = message.Model.Reactions.ToList();
                        list.Add(new Reactions() { Emoji = m.Emoji, Count = 1, Me = m.Me});
                        message.Model.Reactions = list.AsEnumerable();
                    }

                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        message.RaisePropertyChanged(nameof(message.Model));
                    });
                }
            });
            MessengerInstance.Register<GatewayReactionRemovedMessage>(this, async m =>
            {                
                var message = BindableMessages.FirstOrDefault(x => x.Model.Id == m.MessageId);
                if (message != null)
                {
                    var reaction = message.Model.Reactions.FirstOrDefault(x => x.Emoji.Name == m.Emoji.Name && x.Emoji.Id == m.Emoji.Id);
                    if (reaction != null)
                    {
                        reaction.Count--;
                        // TODO: find better update method
                        var list = message.Model.Reactions.ToList();
                        if (reaction.Count == 0)
                        {
                            list.Remove(reaction);
                        }
                        message.Model.Reactions = list.AsEnumerable();
                    }

                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        message.RaisePropertyChanged(nameof(message.Model));
                    });
                }
            });
            MessengerInstance.Register<GatewayGuildChannelUpdatedMessage>(this, async m =>
            {
                // TODO: Complete Update
                DispatcherHelper.CheckBeginInvokeOnUi(() => 
                {
                    GuildsService.GetChannel(m.Channel.Id).Model = m.Channel;
                });
            });
            MessengerInstance.Register<GatewayTypingStartedMessage>(this, async m =>
            {
                DispatcherHelper.CheckBeginInvokeOnUi(() => 
                {
                    if (GuildsService.CurrentChannels.TryGetValue(m.TypingStart.ChannelId, out var bChannel))
                    {
                        if (bChannel.Typers.TryRemove(m.TypingStart.UserId, out var oldTimer))
                        {
                            oldTimer.Dispose();
                        }
                            
                        Timer timer = new Timer((s) =>
                        {
                            if (bChannel.Typers.TryRemove(m.TypingStart.UserId, out var oldUser))
                            {
                                oldUser.Dispose();
                            }

                            DispatcherHelper.CheckBeginInvokeOnUi(() =>
                            {
                                bChannel.RaisePropertyChanged(nameof(bChannel.IsTyping));
                                bChannel.RaisePropertyChanged(nameof(bChannel.TypingText));
                            });
                        }, null, 0, 8 * 1000);

                        bChannel.Typers.TryAdd(m.TypingStart.UserId, timer);
                        
                        DispatcherHelper.CheckBeginInvokeOnUi(() =>
                        {
                            bChannel.RaisePropertyChanged(nameof(bChannel.IsTyping));
                            bChannel.RaisePropertyChanged(nameof(bChannel.TypingText));
                        });
                    }
                });
            });
            MessengerInstance.Register<string>(this, async m =>
            {
                if (m == "GuildsReady")
                {
                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        // Show guilds
                        BindableGuilds.AddRange(GuildsService.Guilds.Values.OrderBy(x => x.Position));
                    });
                }
            });
            MessengerInstance.Register<GuildMembersSyncedMessage>(this, m =>
            {
                DispatcherHelper.CheckBeginInvokeOnUi(() =>
                {
                    // Show guilds
                    BindableMembers.Clear();
                    BindableMembers.AddElementRange(m.Members);
                });
            });
            MessengerInstance.Register<GatewayPresenceUpdatedMessage>(this, m =>
            {
                if (_PresenceDictionary.ContainsKey(m.UserId))
                {
                    _PresenceDictionary[m.UserId] = m.Presence;
                }
                else
                {
                    _PresenceDictionary.TryAdd(m.UserId, m.Presence);
                }
            });
            MessengerInstance.Register<GatewayVoiceStateUpdateMessage>(this, async m =>
            {

                if (m.VoiceState.UserId == DiscordService.CurrentUser.Id)
                {
                    DispatcherHelper.CheckBeginInvokeOnUi(() => VoiceState = m.VoiceState);
                }
            });
            MessengerInstance.Register<CurrentUserVoiceStateRequestMessage>(this, async m =>
            {
                DispatcherHelper.CheckBeginInvokeOnUi(() => m.ReportResult(VoiceState));
            });

            MessengerInstance.Register<PresenceRequestMessage>(this, m => m.ReportResult(_PresenceDictionary.TryGetValue(m.UserId, out var value) ? value : new Presence() { Status = "offline" }));
        }

        public async void Login()
        {
            var token = (string)(await CacheService.Persistent.Roaming.TryGetValueAsync<object>(Quarrel.Helpers.Constants.Cache.Keys.AccessToken));
            if (string.IsNullOrEmpty(token))
            {
                SubFrameNavigationService.NavigateTo("LoginPage");
            }
            else
            {
                DiscordService.Login(token);
            }
        }

        ConcurrentDictionary<string, Presence> _PresenceDictionary = new ConcurrentDictionary<string, Presence>();


        public event EventHandler<BindableMessage> ScrollTo;

        private string guildId
        {
            get => Channel.GuildId;
        }

        static SemaphoreSlim SemaphoreSlim { get; } = new SemaphoreSlim(1, 1);

        #region Commands
        private RelayCommand tiggerTyping;
        public RelayCommand TriggerTyping => tiggerTyping ??= new RelayCommand(() =>
        {
            DiscordService.ChannelService.TriggerTypingIndicator(Channel.Model.Id);
        });

        private RelayCommand sendMessageCommand;
        public RelayCommand SendMessageCommand => sendMessageCommand ??= new RelayCommand(async () =>
        {
            string text = MessageText;
            var mentions = FindMentions(text);
            foreach (var mention in mentions)
            {
                if (mention[0] == '@')
                {
                    int discIndex = mention.IndexOf('#');
                    string username = mention.Substring(1, discIndex - 1);
                    string disc = mention.Substring(1 + discIndex);
                    User user;

                    user = CurrentUsersService.Users.Values.FirstOrDefault(x => x.Model.User.Username == username && x.Model.User.Discriminator == disc).Model.User;

                    if (user != null)
                    {
                        text = text.Replace("@" + user.Username + "#" + user.Discriminator, "<@!" + user.Id + ">");
                    }
                }
                else if (mention[0] == '#')
                {
                    if (!Guild.IsDM)
                    {
                        var channel = Guild.Channels.FirstOrDefault(x => x.Model.Type != 4 && x.Model.Name == mention.Substring(1)).Model;
                        text = text.Replace("#" + channel.Name, "<#" + channel.Id + ">");
                    }
                }
            }
            await DiscordService.ChannelService.CreateMessage(Channel.Model.Id, new DiscordAPI.API.Channel.Models.MessageUpsert() { Content = text });
            DispatcherHelper.CheckBeginInvokeOnUi(() =>
            {
                MessageText = "";
            });
        });
        
        private RelayCommand newLineCommand;
        public RelayCommand NewLineCommand =>
            newLineCommand ??= new RelayCommand(() =>
            {
                string text = MessageText;
                int selectionstart = SelectionStart;

                if (SelectionLength > 0)
                {
                    // Remove selected text first
                    text = text.Remove(selectionstart, SelectionLength);
                }

                text = text.Insert(selectionstart, Environment.NewLine + Environment.NewLine); //Not sure why two lines breaks are needed but it doesn't work otherwise
                MessageText = text;
                SelectionStart = selectionstart + 1;
            });

        private RelayCommand<BindableGuild> navigateGuildCommand;
        public RelayCommand<BindableGuild> NavigateGuildCommand => navigateGuildCommand ??= new RelayCommand<BindableGuild>((guild) =>
        {
            MessengerInstance.Send(new GuildNavigateMessage(guild));
        });

        private RelayCommand<BindableChannel> navigateChannelCommand;
        public RelayCommand<BindableChannel> NavigateChannelCommand => navigateChannelCommand ??= new RelayCommand<BindableChannel>(async (channel) =>
        {
            Channel = channel;
            if (channel.IsCategory)
            {
                bool newState = !channel.Collapsed;
                for (int i = BindableChannels.IndexOf(channel);
                    i < BindableChannels.Count
                    && BindableChannels[i] != null
                    && BindableChannels[i].ParentId == channel.Model.Id;
                    i++)
                {
                    BindableChannels[i].Collapsed = newState;
                }
            }
            else if (channel.IsVoiceChannel)
            {
                if (channel.Model is GuildChannel gChannel)
                    await DiscordService.Gateway.Gateway.VoiceStatusUpdate(Guild.Model.Id, gChannel.Id, false, false);
            }
            else if (channel.Permissions.ReadMessages)
            {
                MessengerInstance.Send(new ChannelNavigateMessage(channel, Guild));
            }
        });

        private RelayCommand disconnectVoiceCommand;
        public RelayCommand DisconnectVoiceCommand => disconnectVoiceCommand ??= new RelayCommand(async () =>
        {
            await GatewayService.Gateway.VoiceStatusUpdate(null, null, false, false);
        });
        #endregion

        #region Methods
        private List<string> FindMentions(string message)
        {
            List<string> mentions = new List<string>();
            bool inMention = false;
            bool inDesc = false;
            bool inChannel = false;
            string cache = "";
            string descCache = "";
            string chnCache = "";
            foreach (char c in message)
            {
                if (inMention)
                {
                    if (c == '#' && !inDesc)
                    {
                        inDesc = true;
                    }
                    else if (c == '@')
                    {
                        inDesc = false;
                        cache = "";
                        descCache = "";
                    }
                    else if (inDesc)
                    {
                        if (char.IsDigit(c))
                        {
                            descCache += c;
                        }
                        else
                        {
                            inMention = false;
                            inDesc = false;
                            cache = "";
                            descCache = "";
                        }
                        if (descCache.Length == 4)
                        {
                            User mention;
                            if (Channel.Model is DirectMessageChannel dmChn)
                            {
                                mention = dmChn.Users.FirstOrDefault(x => x.Username == cache && x.Discriminator == descCache);
                            }
                            else
                            {
                                GuildMember member = CurrentUsersService.Users.Values
                                           .FirstOrDefault(x => x.Model.User.Username == cache && x.Model.User.Discriminator == descCache).Model;
                                mention = member.User;
                            }
                            if (mention != null)
                            {
                                mentions.Add("@" + cache + "#" + descCache);
                            }
                            inMention = false;
                            inDesc = false;
                            cache = "";
                            descCache = "";
                        }
                    }
                    else
                    {
                        cache += c;
                    }
                }
                else if (inChannel)
                {
                    if (c == ' ')
                    {
                        inChannel = false;
                        chnCache = "";
                    }
                    else
                    {
                        chnCache += c;
                        if (Channel.Model is GuildChannel)
                        {
                            if (!Guild.IsDM)
                            {
                                mentions.Add("#" + chnCache);
                            }
                        }
                    }
                }
                else if (c == '@')
                {
                    inMention = true;
                }
                else if (c == '#')
                {
                    inChannel = true;
                }
            }
            return mentions;
        }

        public async void LoadOlderMessages()
        {
            if (ItemsLoading) return;
            await SemaphoreSlim.WaitAsync();
            try
            {
                OldItemsLoading = true;
                IEnumerable<Message> itemList =
                    await DiscordService.ChannelService.GetChannelMessagesBefore(Channel.Model.Id,
                        BindableMessages.FirstOrDefault().Model.Id);

                List<BindableMessage> messages = new List<BindableMessage>();
                Message lastItem = null;
                foreach (var item in itemList.Reverse())
                {
                    // Can't be last read item
                    messages.Add(new BindableMessage(item, guildId, lastItem));
                    lastItem = item;
                }

                DispatcherHelper.CheckBeginInvokeOnUi(() => { BindableMessages.InsertRange(0, messages, NotifyCollectionChangedAction.Reset); });
                OldItemsLoading = false;
            }
            finally
            {
                SemaphoreSlim.Release();
            }
        }

        public async void LoadNewerMessages()
        {
            if (ItemsLoading) return;
            await SemaphoreSlim.WaitAsync();
            try
            {
                NewItemsLoading = true;
                if (Channel.Model.LastMessageId != BindableMessages.LastOrDefault().Model.Id)
                {
                    IEnumerable<Message> itemList = null;
                    await Task.Run(async () => itemList = await DiscordService.ChannelService.GetChannelMessagesAfter(Channel.Model.Id, BindableMessages.LastOrDefault().Model.Id));

                    List<BindableMessage> messages = new List<BindableMessage>();
                    Message lastItem = null;
                    foreach (var item in itemList)
                    {
                        // Can't be last read item
                        messages.Add(new BindableMessage(item, guildId, lastItem));
                        lastItem = item;
                    }
                    DispatcherHelper.CheckBeginInvokeOnUi(() =>
                    {
                        BindableMessages.AddRange(messages);
                    });
                }
                else if (Channel.ReadState == null || Channel.Model.LastMessageId != Channel.ReadState.LastMessageId)
                {
                    await DiscordService.ChannelService.AckMessage(Channel.Model.Id, BindableMessages.LastOrDefault().Model.Id);
                }
                NewItemsLoading = false;
            }
            finally
            {
                SemaphoreSlim.Release();
            }
        }
        #endregion

        #region Properties

        private BindableGuild _Guild;
        public BindableGuild Guild
        {
            get => _Guild;
            set => Set(ref _Guild, value);
        }

        private bool _NewItemsLoading;
        public bool NewItemsLoading
        {
            get => _NewItemsLoading;
            set => Set(ref _NewItemsLoading, value);
        }

        private bool _OldItemsLoading;
        public bool OldItemsLoading
        {
            get => _OldItemsLoading;
            set => Set(ref _OldItemsLoading, value);
        }


        private bool ItemsLoading => _NewItemsLoading || _OldItemsLoading;


        private BindableChannel _Channel;
        public BindableChannel Channel
        {
            get => _Channel;
            set => Set(ref _Channel, value);
        }

        private string _MessageText = "";

        public string MessageText
        {
            get => _MessageText;
            set => Set(ref _MessageText, value);
        }

        private int _SelectionStart;

        public int SelectionStart
        {
            get => _SelectionStart;
            set => Set(ref _SelectionStart, value);
        }

        private int _SelectionLength;

        public int SelectionLength
        {
            get => _SelectionLength;
            set => Set(ref _SelectionLength, value);
        }

        private VoiceState voiceState = new VoiceState();

        public VoiceState VoiceState
        {
            get => voiceState;
            set => Set(ref voiceState, value);
        }

        [NotNull]
        public ObservableRangeCollection<BindableGuild> BindableGuilds { get; private set; } = new ObservableRangeCollection<BindableGuild>();
        /// <summary>
        /// Gets the collection of grouped feeds to display
        /// </summary>
        [NotNull]
        public ObservableRangeCollection<BindableMessage> BindableMessages { get; private set; } = new ObservableRangeCollection<BindableMessage>();
        [NotNull]
        public ObservableRangeCollection<BindableChannel> BindableChannels { get; private set; } = new ObservableRangeCollection<BindableChannel>();
        [NotNull]
        public ObservableSortedGroupedCollection<Role, BindableGuildMember> BindableMembers { get; set; } = new ObservableSortedGroupedCollection<Role, BindableGuildMember>(x => x.TopHoistRole, x => -x.Position);
        #endregion
    }
}
