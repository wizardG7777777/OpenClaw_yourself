public interface IUIPanel
{
    bool IsExclusive { get; }
    bool IsOpen { get; }
    void Open();
    void Close();
}
