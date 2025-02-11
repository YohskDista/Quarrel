﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DiscordAPI.Models;
using GalaSoft.MvvmLight.Command;
using JetBrains.Annotations;
using Quarrel.Models.Bindables.Abstract;

namespace Quarrel.Models.Bindables
{
    public class BindableVideoEmbed : BindableModelBase<Embed>
    {
        private bool playingVideo;
        public bool PlayingVideo
        {
            get => playingVideo;
            set
            {
                Set(ref playingVideo, value);
                RaisePropertyChanged(nameof(NotPlayingVideo));
            }
        }

        public bool NotPlayingVideo
        {
            get => !playingVideo;
            set
            {
                Set(ref playingVideo, !value);
                RaisePropertyChanged(nameof(PlayingVideo));
            }
        }

        private RelayCommand playVideoCommand;

        public RelayCommand PlayVideoCommand => playVideoCommand ?? (playVideoCommand = new RelayCommand(() =>
         {
             PlayingVideo = true;
         }));

        public BindableVideoEmbed([NotNull] Embed model) : base(model)
        {
        }


    }
}
