using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Item
{
    public string itemId;
    public string displayName;
    public string description;
    public ItemType itemType;
    public Sprite icon;
    public int quantity;
    public int maxStack;
    public bool isUsable;
    public bool isEquippable;

    private List<IItemAction> _actions = new List<IItemAction>();
    public IReadOnlyList<IItemAction> Actions => _actions;

    public Item(string itemId, string displayName, string description,
                ItemType itemType, int quantity = 1, int maxStack = 99,
                bool isUsable = false, bool isEquippable = false)
    {
        this.itemId = itemId;
        this.displayName = displayName;
        this.description = description;
        this.itemType = itemType;
        this.quantity = quantity;
        this.maxStack = maxStack;
        this.isUsable = isUsable;
        this.isEquippable = isEquippable;
    }

    public void AddAction(IItemAction action)
    {
        _actions.Add(action);
    }

    public void RemoveAction(IItemAction action)
    {
        _actions.Remove(action);
    }
}
