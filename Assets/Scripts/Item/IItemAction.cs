using UnityEngine;

public interface IItemAction
{
    string ActionName { get; }
    bool CanExecute(Item item, GameObject target);
    void Execute(Item item, GameObject target);
}
