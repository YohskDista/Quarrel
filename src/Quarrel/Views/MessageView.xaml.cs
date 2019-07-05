﻿using GalaSoft.MvvmLight.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Quarrel.Messages.Navigation;
using UICompositionAnimations.Helpers;
using Quarrel.Models.Bindables;
using Quarrel.ViewModels;
using DiscordAPI.Models;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace Quarrel.Views
{
    public sealed partial class MessageView : UserControl
    {
        public MessageView()
        {
            this.InitializeComponent();
            ViewModel.ScrollTo += ViewModel_ScrollTo;
            Messenger.Default.Register<ChannelNavigateMessage>(this, m =>
            {
                _ItemsStackPanel.ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView;
            });
        }

        private ItemsStackPanel _ItemsStackPanel;
        private ScrollViewer _MessageScrollViewer;

        private void ItemsStackPanel_Loaded(object sender, RoutedEventArgs e)
        {
            _MessageScrollViewer = MessageList.FindChild<ScrollViewer>();
            _ItemsStackPanel = (sender as ItemsStackPanel);
            _ItemsStackPanel.ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepItemsInView;
            if (_MessageScrollViewer != null) _MessageScrollViewer.ViewChanged += _messageScrollViewer_ViewChanged; ;
        }

        private void _messageScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (MessageList.Items.Count > 0)
            {
                // Distance from top
                double fromTop = _MessageScrollViewer.VerticalOffset;

                //Distance from bottom
                double fromBottom = _MessageScrollViewer.ScrollableHeight - fromTop;

                // Load messages
                if (fromTop < 100)
                    ViewModel.LoadOlderMessages();
                if (fromBottom < 200)
                    ViewModel.LoadNewerMessages();
            }
        }

        private void ViewModel_ScrollTo(object sender, KeyValuePair<string, BindableMessage> e)
        {
            MessageList.ScrollIntoView(e);
        }
    }
}
