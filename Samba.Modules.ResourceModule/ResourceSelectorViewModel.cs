﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using Microsoft.Practices.Prism.Commands;
using Samba.Domain.Models.Resources;
using Samba.Infrastructure;
using Samba.Localization.Properties;
using Samba.Presentation.Common;
using Samba.Presentation.Common.Services;
using Samba.Services;
using Samba.Services.Common;

namespace Samba.Modules.ResourceModule
{
    [Export]
    public class ResourceSelectorViewModel : ObservableObject
    {
        public DelegateCommand<ResourceScreenItemWidget> ResourceSelectionCommand { get; set; }
        public ICaptionCommand EditSelectedResourceScreenPropertiesCommand { get; set; }
        public ICaptionCommand IncPageNumberCommand { get; set; }
        public ICaptionCommand DecPageNumberCommand { get; set; }
        public ObservableCollection<IDiagram> ResourceScreenItems { get; set; }
        public ResourceScreen SelectedResourceScreen { get { return _applicationState.SelectedResourceScreen; } }
        //public bool CanDesignResourceScreenItems { get { return _applicationState.CurrentLoggedInUser.UserRole.IsAdmin; } }
        public int CurrentPageNo { get; set; }
        public bool IsPageNavigatorVisible { get { return SelectedResourceScreen != null && SelectedResourceScreen.PageCount > 1; } }

        public VerticalAlignment ScreenVerticalAlignment { get { return SelectedResourceScreen != null && SelectedResourceScreen.ButtonHeight > 0 ? VerticalAlignment.Top : VerticalAlignment.Stretch; } }

        private readonly IApplicationState _applicationState;
        private readonly IApplicationStateSetter _applicationStateSetter;
        private readonly IResourceService _resourceService;
        private readonly IUserService _userService;
        private readonly ICacheService _cacheService;
        private readonly ITicketService _ticketService;
        private EntityOperationRequest<Resource> _currentOperationRequest;

        [ImportingConstructor]
        public ResourceSelectorViewModel(IApplicationState applicationState, IApplicationStateSetter applicationStateSetter,
            IResourceService resourceService, IUserService userService, ICacheService cacheService, ITicketService ticketService)
        {
            _applicationState = applicationState;
            _applicationStateSetter = applicationStateSetter;
            _resourceService = resourceService;
            _userService = userService;
            _cacheService = cacheService;
            _ticketService = ticketService;

            ResourceSelectionCommand = new DelegateCommand<ResourceScreenItemWidget>(OnSelectResourceExecuted);
            EditSelectedResourceScreenPropertiesCommand = new CaptionCommand<string>(Resources.Properties, OnEditSelectedResourceScreenProperties, CanEditSelectedResourceScreenProperties);
            IncPageNumberCommand = new CaptionCommand<string>(Resources.NextPage + " >>", OnIncPageNumber, CanIncPageNumber);
            DecPageNumberCommand = new CaptionCommand<string>("<< " + Resources.PreviousPage, OnDecPageNumber, CanDecPageNumber);

            EventServiceFactory.EventService.GetEvent<GenericEvent<Message>>().Subscribe(
                x =>
                {
                    if (_applicationState.ActiveAppScreen == AppScreens.ResourceView
                        && x.Topic == EventTopicNames.MessageReceivedEvent
                        && x.Value.Command == Messages.TicketRefreshMessage)
                    {
                        RefreshResourceScreenItems();
                    }
                });
        }

        public void RefreshResourceScreenItems()
        {
            if (SelectedResourceScreen != null)
                UpdateResourceScreenItems(SelectedResourceScreen);
        }

        private bool CanDecPageNumber(string arg)
        {
            return SelectedResourceScreen != null && CurrentPageNo > 0;
        }

        private void OnDecPageNumber(string obj)
        {
            CurrentPageNo--;
            RefreshResourceScreenItems();
        }

        private bool CanIncPageNumber(string arg)
        {
            return SelectedResourceScreen != null && CurrentPageNo < SelectedResourceScreen.PageCount - 1;
        }

        private void OnIncPageNumber(string obj)
        {
            CurrentPageNo++;
            RefreshResourceScreenItems();
        }

        private bool CanEditSelectedResourceScreenProperties(string arg)
        {
            return SelectedResourceScreen != null;
        }

        private void OnEditSelectedResourceScreenProperties(string obj)
        {
            if (SelectedResourceScreen != null)
                InteractionService.UserIntraction.EditProperties(SelectedResourceScreen);
        }

        private void OnSelectResourceExecuted(ResourceScreenItemWidget obj)
        {
            if (obj.Model.ResourceId > 0 && obj.Model.ItemId == 0)
                _currentOperationRequest.Publish(_cacheService.GetResourceById(obj.Model.ResourceId));
            else if (obj.Model.ItemId > 0)
            {
                ExtensionMethods.PublishIdEvent(obj.Model.ItemId, EventTopicNames.DisplayTicket);
            }
        }

        private void UpdateResourceScreenItems(ResourceScreen resourceScreen)
        {
            var stateFilter = resourceScreen.DisplayMode == 0 ? resourceScreen.StateFilterId : 0;
            var resourceData = GetResourceScreenItems(resourceScreen, stateFilter);
            if (ResourceScreenItems != null && (ResourceScreenItems.Count() == 0 || ResourceScreenItems.Count != resourceData.Count() || ResourceScreenItems.First().Caption != resourceData.First().Name)) ResourceScreenItems = null;

            ClearCustomItems();
            UpdateResourceButtons(resourceData);
            AddOpenTickets(resourceScreen);

            RaisePropertyChanged(() => ResourceScreenItems);
            RaisePropertyChanged(() => SelectedResourceScreen);
            RaisePropertyChanged(() => IsPageNavigatorVisible);
            RaisePropertyChanged(() => ScreenVerticalAlignment);
        }

        private List<ResourceScreenItem> GetResourceScreenItems(ResourceScreen resourceScreen, int stateFilter)
        {
            _applicationStateSetter.SetSelectedResourceScreen(resourceScreen);
            if (resourceScreen.ScreenItems.Count > 0)
                return _resourceService.GetCurrentResourceScreenItems(resourceScreen, CurrentPageNo, stateFilter).OrderBy(x => x.Order).ToList();
            return
                _resourceService.GetResourcesByState(stateFilter, resourceScreen.ResourceTemplateId).Select(x => new ResourceScreenItem { ResourceId = x.Id, Name = x.Name, ResourceStateId = stateFilter }).ToList();
        }

        private void UpdateResourceButtons(ICollection<ResourceScreenItem> resourceData)
        {
            if (ResourceScreenItems == null)
            {
                if (SelectedResourceScreen.RowCount > 0 && SelectedResourceScreen.ColumnCount > 0 && resourceData.Count > SelectedResourceScreen.ColumnCount * SelectedResourceScreen.RowCount)
                    SelectedResourceScreen.RowCount = 0;
                ResourceScreenItems = new ObservableCollection<IDiagram>();
                ResourceScreenItems.AddRange(resourceData.Select(x => new
                    ResourceScreenItemWidget(x, SelectedResourceScreen, ResourceSelectionCommand,
                    _currentOperationRequest.SelectedEntity != null, _userService.IsUserPermittedFor(PermissionNames.MergeTickets),
                    _cacheService.GetResourceStateById(x.ResourceStateId))));
            }
            else
            {
                for (var i = 0; i < resourceData.Count(); i++)
                {
                    var acs = ((ResourceScreenItemWidget)ResourceScreenItems[i]).ResourceState;
                    if (acs == null || acs.Id != resourceData.ElementAt(i).ResourceStateId)
                        ((ResourceScreenItemWidget)ResourceScreenItems[i]).ResourceState = _cacheService.GetResourceStateById(resourceData.ElementAt(i).ResourceStateId);
                    ((ResourceScreenItemWidget)ResourceScreenItems[i]).Model = resourceData.ElementAt(i);
                }
            }
        }

        private void ClearCustomItems()
        {
            if (ResourceScreenItems != null && SelectedResourceScreen.DisplayMode == 0)
                ResourceScreenItems.Cast<ResourceScreenItemWidget>().Where(x => x.Model.ResourceId == 0).ToList().ForEach(
                    x => ResourceScreenItems.Remove(x));
        }

        private void AddOpenTickets(ResourceScreen resourceScreen)
        {
            if (resourceScreen.DisplayMode == 0 && resourceScreen.DisplayOpenTickets && _currentOperationRequest.SelectedEntity == null)
            {
                var openTickets =
                    _ticketService.GetOpenTickets(
                        x =>
                        x.RemainingAmount > 0 &&
                        x.DepartmentId == _applicationState.CurrentDepartment.Id &&
                        !x.TicketResources.Any(y => y.ResourceTemplateId == resourceScreen.ResourceTemplateId)).ToList();
                if (openTickets.Count > 0)
                {
                    ResourceScreenItems.AddRange(openTickets.Select(x => new ResourceScreenItemWidget(
                                                                             new ResourceScreenItem(x.Id) { Name = x.TicketNumber }, resourceScreen,
                                                                             ResourceSelectionCommand,
                                                                             _currentOperationRequest.SelectedEntity != null,
                                                                             _userService.IsUserPermittedFor(
                                                                                 PermissionNames.MergeTickets),
                                                                             ResourceState.Empty)));
                }
            }
        }

        public void LoadTrackableResourceScreenItems()
        {
            ResourceScreenItems = new ObservableCollection<IDiagram>(
                _resourceService.LoadResourceScreenItems(SelectedResourceScreen.Name)
                .Select<ResourceScreenItem, IDiagram>(x => new ResourceScreenItemWidget(x, SelectedResourceScreen)));
            RaisePropertyChanged(() => ResourceScreenItems);
        }

        public void SaveTrackableResourceScreenItems()
        {
            _resourceService.SaveResourceScreenItems();
            UpdateResourceScreenItems(SelectedResourceScreen);
        }

        public void Refresh(ResourceScreen resourceScreen, EntityOperationRequest<Resource> currentOperationRequest)
        {
            _currentOperationRequest = currentOperationRequest;
            UpdateResourceScreenItems(resourceScreen);
        }
    }
}
