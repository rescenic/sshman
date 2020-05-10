﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;

namespace SSHMan
{
    class HostModel : DefaultModel
    {
        private readonly SSHParser parser = new SSHParser();

        public HostModel() => this.ReadConfig();


        private void ReadConfig()
        {
            Items.Clear();
            RaisePropertyChanged(() => Items);
            var hostMap = parser.HostMap();
            foreach ((_, var config) in hostMap)
            {
                Items.Add(config.ToEntry());
            }
        }


        public void Clear()
        {
            Items.Clear();
            RaisePropertyChanged(() => Items);
        }
        

        public void Update()
        {
            ReadConfig();
            RaisePropertyChanged(() => Items);
        }

        public ObservableCollection<SSHHostEntry> Items { get; } = new ObservableCollection<SSHHostEntry>();
    }
}
