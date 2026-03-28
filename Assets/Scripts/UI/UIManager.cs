using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    private IUIPanel _activePanel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Attempts to open a panel. Returns false if blocked by an exclusive panel.
    /// </summary>
    public bool RequestOpen(IUIPanel panel)
    {
        if (panel == null) return false;

        // If an exclusive panel is active, block everything else
        if (_activePanel != null && _activePanel.IsOpen && _activePanel.IsExclusive && _activePanel != panel)
            return false;

        // Close current panel if it's a different one
        if (_activePanel != null && _activePanel != panel && _activePanel.IsOpen)
            _activePanel.Close();

        _activePanel = panel;
        panel.Open();
        return true;
    }

    /// <summary>
    /// Notifies UIManager that a panel has closed.
    /// </summary>
    public void NotifyClose(IUIPanel panel)
    {
        if (_activePanel == panel)
            _activePanel = null;
    }

    /// <summary>
    /// Returns true if any exclusive panel is currently open.
    /// </summary>
    public bool IsBlocked()
    {
        return _activePanel != null && _activePanel.IsOpen && _activePanel.IsExclusive;
    }
}
