﻿using Microsoft.AspNetCore.Components;
using OneDas.DataManagement.Explorer.ViewModels;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace OneDas.DataManagement.Explorer.Core
{
	public abstract class UserStateComponentBase : ComponentBase, IDisposable
	{
		#region Properties

		[Inject]
		public AppStateViewModel AppState { get; set; }

		[Inject]
		public UserStateViewModel UserState { get; set; }

		protected PropertyChangedEventHandler PropertyChanged { get; set; }

		#endregion

		#region Methods

		protected override Task OnParametersSetAsync()
		{
			this.AppState.PropertyChanged += this.PropertyChanged;
			this.UserState.PropertyChanged += this.PropertyChanged;

			return base.OnParametersSetAsync();
		}

		#endregion

		#region IDisposable Support

		private bool disposedValue = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					this.AppState.PropertyChanged -= this.PropertyChanged;
					this.UserState.PropertyChanged -= this.PropertyChanged;
				}

				disposedValue = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}

		#endregion
	}
}
