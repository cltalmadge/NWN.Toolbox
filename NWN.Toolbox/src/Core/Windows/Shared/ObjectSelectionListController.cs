using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Anvil.API;
using Anvil.API.Events;
using Action = System.Action;

namespace Jorteck.Toolbox.Core
{
  public sealed class ObjectSelectionListController
  {
    private List<NwGameObject> objectList;
    private Color[] rowColors;

    public NwObject SelectedObject { get; private set; }
    private NwArea selectedArea;

    private readonly ObjectSelectionListView view;
    private readonly NuiWindowToken windowToken;
    private TimeSpan lastSelectionClick;

    public event Action OnObjectSelectChange;

    public bool RestrictTypeSelection
    {
      get => !windowToken.GetBindValue(view.UnrestrictedType);
      set => windowToken.SetBindValue(view.UnrestrictedType, !value);
    }

    public bool RestrictAreaSelection
    {
      get => !windowToken.GetBindValue(view.UnrestrictedArea);
      set => windowToken.SetBindValue(view.UnrestrictedArea, !value);
    }

    public ObjectSelectionListController(ObjectSelectionListView view, NuiWindowToken windowToken)
    {
      this.view = view;
      this.windowToken = windowToken;

      RestrictTypeSelection = false;
      RestrictAreaSelection = false;
    }

    public void Init(NwArea area, ObjectSelectionTypes initialSelectionTypes = ObjectSelectionTypes.Creature)
    {
      windowToken.SetBindValue(view.SearchObjectType, (int)initialSelectionTypes);
      UpdateAreaSelection(area);
      RefreshObjectList();
    }

    public bool ProcessEvent(ModuleEvents.OnNuiEvent eventData)
    {
      switch (eventData.EventType)
      {
        case NuiEventType.Click:
          return HandleButtonClick(eventData);
        case NuiEventType.MouseDown:
          return HandleMouseDown(eventData);
        default:
          return false;
      }
    }

    public void Refresh()
    {
      if (SelectedObject != null && !SelectedObject.IsValid)
      {
        ClearObjectSelection();
      }

      RefreshObjectList();
    }

    private bool HandleMouseDown(ModuleEvents.OnNuiEvent eventData)
    {
      if (eventData.ElementId == view.ObjectRowId)
      {
        SelectObject(eventData.ArrayIndex);
        return true;
      }

      return false;
    }

    private bool HandleButtonClick(ModuleEvents.OnNuiEvent eventData)
    {
      if (eventData.ElementId == view.SearchButton.Id)
      {
        RefreshObjectList();
        return true;
      }

      if (eventData.ElementId == view.ObjectPickerButton.Id)
      {
        windowToken.Player.EnterTargetMode(eventData =>
        {
          if (eventData.TargetObject is NwGameObject gameObject)
          {
            UpdateSelection(gameObject);
          }
        });
      }

      return false;
    }

    private void RefreshObjectList()
    {
      if (selectedArea == null)
      {
        return;
      }

      objectList = LoadSearchResults();
      UpdateListViewBinds(objectList);
    }

    private List<NwGameObject> LoadSearchResults()
    {
      string search = windowToken.GetBindValue(view.Search);
      ObjectSelectionTypes searchTypes = (ObjectSelectionTypes)windowToken.GetBindValue(view.SearchObjectType);

      List<NwGameObject> results = new List<NwGameObject>();

      NwCreature playerCreature = windowToken.Player.ControlledCreature!;

      NwArea playerArea = playerCreature.Area;
      Vector3 playerPos = playerCreature.Position;

      float distance = windowToken.GetBindValue(view.SearchDistance)!.ParseFloat(0f);
      float? distanceSqr = distance == 0 ? null : distance * distance;

      if (searchTypes.HasFlag(ObjectSelectionTypes.Player))
      {
        foreach (NwPlayer player in NwModule.Instance.Players)
        {
          NwCreature controlledCreature = player.ControlledCreature;
          if (controlledCreature?.Area != playerArea)
          {
            continue;
          }

          if (ShouldListObject(controlledCreature, playerArea, playerPos, search, searchTypes | ObjectSelectionTypes.Creature, distanceSqr))
          {
            results.Add(player.ControlledCreature);
          }
        }
      }

      foreach (NwGameObject gameObject in selectedArea.Objects)
      {
        if (ShouldListObject(gameObject, playerArea, playerPos, search, searchTypes, distanceSqr))
        {
          results.Add(gameObject);
        }
      }

      return results;
    }

    private bool ShouldListObject(NwGameObject gameObject, NwArea playerArea, Vector3 playerPos, string search, ObjectSelectionTypes selectionTypes, float? distanceSqr = null)
    {
      if (gameObject == null || !gameObject.IsValid)
      {
        return false;
      }

      ObjectSelectionTypes selectionType = gameObject.GetSelectionType();
      bool hasFlag = selectionTypes.HasFlag(selectionType);
      if (string.IsNullOrEmpty(search) && distanceSqr == null && hasFlag)
      {
        return true;
      }

      return hasFlag && gameObject.Name.Contains(search ?? string.Empty, StringComparison.OrdinalIgnoreCase)
        && (distanceSqr == null || playerArea == gameObject.Area && Vector3.DistanceSquared(playerPos, gameObject.Position) < distanceSqr);
    }

    private void UpdateListViewBinds(List<NwGameObject> gameObjects)
    {
      rowColors = new Color[gameObjects.Count];

      string[] objectTypes = new string[gameObjects.Count];
      string[] objectNames = new string[gameObjects.Count];
      string[] objectResRefs = new string[gameObjects.Count];
      string[] objectTags = new string[gameObjects.Count];
      string[] objectHPs = new string[gameObjects.Count];
      string[] objectCRs = new string[gameObjects.Count];

      for (int i = 0; i < gameObjects.Count; i++)
      {
        NwGameObject gameObject = gameObjects[i];
        rowColors[i] = gameObject != SelectedObject ? UXConstants.DefaultColor : UXConstants.SelectedColor;
        objectTypes[i] = gameObject.GetTypeName();
        objectNames[i] = gameObject.Name;
        objectResRefs[i] = gameObject.ResRef;
        objectTags[i] = gameObject.Tag;
        objectHPs[i] = $"{gameObject.HP}/{gameObject.MaxHP}";
        objectCRs[i] = gameObject is NwCreature creature ? creature.ChallengeRating.ToString(CultureInfo.InvariantCulture) : "";
      }

      windowToken.SetBindValues(view.RowColors, rowColors);
      windowToken.SetBindValues(view.ObjectTypes, objectTypes);
      windowToken.SetBindValues(view.ObjectNames, objectNames);
      windowToken.SetBindValues(view.ObjectResRefs, objectResRefs);
      windowToken.SetBindValues(view.ObjectTags, objectTags);
      windowToken.SetBindValues(view.ObjectHPs, objectHPs);
      windowToken.SetBindValues(view.ObjectCRs, objectCRs);
      windowToken.SetBindValue(view.ObjectCount, gameObjects.Count);
    }

    private void SelectObject(int index)
    {
      if (index >= 0 && index < objectList.Count)
      {
        NwGameObject newSelection = objectList[index];
        if (newSelection != SelectedObject)
        {
          UpdateSelection(newSelection);
        }
        else if (Time.TimeSinceStartup - lastSelectionClick < UXConstants.DoubleClickThreshold)
        {
          JumpToObject(newSelection);
        }

        lastSelectionClick = Time.TimeSinceStartup;
      }
    }

    public async void JumpToObject(NwGameObject gameObject)
    {
      await windowToken.Player.ControlledCreature!.JumpToObject(gameObject);
    }

    private void UpdateSelection(NwObject newSelection)
    {
      if (newSelection == SelectedObject)
      {
        return;
      }

      ResetExistingSelection();
      if (newSelection is NwGameObject gameObject)
      {
        int index = objectList.IndexOf(gameObject);
        if (index >= 0)
        {
          rowColors[index] = UXConstants.SelectedColor;
        }
      }

      windowToken.SetBindValues(view.RowColors, rowColors);
      SelectedObject = newSelection;
      OnObjectSelectChange?.Invoke();
    }

    private void ClearObjectSelection()
    {
      ResetExistingSelection();
      SelectedObject = null;
      OnObjectSelectChange?.Invoke();
    }

    private void ResetExistingSelection()
    {
      if (SelectedObject != null && SelectedObject is NwGameObject gameObject)
      {
        int existingSelection = objectList.IndexOf(gameObject);
        if (existingSelection >= 0)
        {
          rowColors[existingSelection] = UXConstants.DefaultColor;
        }
      }
    }

    private void UpdateAreaSelection(NwArea area)
    {
      selectedArea = area;
      windowToken.SetBindValue(view.CurrentArea, area.Name);
    }
  }
}
