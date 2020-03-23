﻿using Microsoft.AspNetCore.Identity;
using OneDas.DataManagement.Explorer.Core;
using Prism.Mvvm;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace OneDas.DataManagement.Explorer.ViewModels
{
#warning: update user cookie when modifying claims
    public class SettingsViewModel : BindableBase
    {
        #region Fields

        private IdentityUser _user;
        private List<ClaimViewModel> _claims;
        private UserManager<IdentityUser> _userManager;

        #endregion

        #region Constructors

        public SettingsViewModel(UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
        }

        #endregion

        #region Properties - General

        public IdentityUser User
        {
            get 
            {
                return _user; 
            }
            set 
            {
                this.UpdateClaims(value);
                base.SetProperty(ref _user, value); 
            }
        }

        public List<IdentityUser> Users => _userManager.Users.ToList();

        public List<ClaimViewModel> Claims
        {
            get { return _claims; }
            set { base.SetProperty(ref _claims, value); }
        }

        #endregion

        #region Methods

        public void RemoveClaim(ClaimViewModel claim)
        {
            this.Claims.Remove(claim);
            this.RaisePropertyChanged(nameof(SettingsViewModel.Claims));
        }

        public async void SaveChanges()
        {
            var claimsToRemove = await _userManager.GetClaimsAsync(this.User);
            var claimsToAdd = this.Claims.Where(claim => !string.IsNullOrWhiteSpace(claim.Type)).Select(claim => claim.ToClaim());

            await _userManager.RemoveClaimsAsync(this.User, claimsToRemove);
            await _userManager.AddClaimsAsync(this.User, claimsToAdd);
        }

        private void UpdateClaims(IdentityUser user)
        {
            var claims = _userManager.GetClaimsAsync(user).Result.ToList();
            claims.Add(new Claim(string.Empty, string.Empty));

            this.Claims = claims.Select(claim => new ClaimViewModel(claim)).ToList();
        }

        #endregion
    }
}