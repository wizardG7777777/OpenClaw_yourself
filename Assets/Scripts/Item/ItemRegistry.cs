using System.Collections.Generic;
using UnityEngine;

public class ItemRegistry : MonoBehaviour
{
    public static ItemRegistry Instance { get; private set; }

    private readonly Dictionary<string, Item> _items = new Dictionary<string, Item>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        RegisterDefaultItems();
    }

    private void RegisterDefaultItems()
    {
        Register(new Item("wrench", "扳手", "用于拧紧或拧松螺栓的工具。",
            ItemType.Tool, isUsable: true, isEquippable: true));

        Register(new Item("shovel", "铲子", "用于挖掘土壤的工具。",
            ItemType.Tool, isUsable: true, isEquippable: true));

        Register(new Item("postcard", "明信片", "一张写满回忆的明信片。",
            ItemType.KeyItem, isUsable: false, isEquippable: false));
    }

    public void Register(Item item)
    {
        _items[item.itemId] = item;
    }

    public Item GetById(string itemId)
    {
        _items.TryGetValue(itemId, out Item item);
        return item;
    }

    public bool Contains(string itemId)
    {
        return _items.ContainsKey(itemId);
    }

    public IEnumerable<Item> GetAll()
    {
        return _items.Values;
    }
}
