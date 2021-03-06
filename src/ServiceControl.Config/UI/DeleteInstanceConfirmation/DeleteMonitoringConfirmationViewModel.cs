﻿using System.Windows.Input;
using ServiceControl.Config.Framework;
using ServiceControl.Config.Framework.Rx;

namespace ServiceControl.Config.UI.DeleteInstanceConfirmation
{
    class DeleteMonitoringConfirmationViewModel : RxScreen
    {
        public DeleteMonitoringConfirmationViewModel()
        {
            RemoveCommand = Command.Create(() => TryClose(true));
            CancelCommand = Command.Create(() => TryClose(false));
        }

        public string InstanceName { get; set; }

        public bool RemoveLogs { get; set; }

        public ICommand RemoveCommand { get; set; }
        public ICommand CancelCommand { get; set; }
    }
}