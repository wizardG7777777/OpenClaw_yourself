using UnityEngine;
using MCP.Core;

namespace MCP.Executor
{
    public class ExecutionManager : MonoBehaviour
    {
        public static ExecutionManager Instance { get; private set; }

        private IActionHandler activeHandler;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void StartHandler(IActionHandler handler, ActionInstance action)
        {
            if (activeHandler != null)
            {
                CancelCurrent();
            }

            activeHandler = handler;
            handler.StartAction(action);
        }

        private void Update()
        {
            if (activeHandler == null)
                return;

            activeHandler.UpdateAction();

            if (activeHandler.IsComplete)
            {
                activeHandler = null;
            }
        }

        public void CancelCurrent()
        {
            if (activeHandler != null)
            {
                activeHandler.Cancel();
                activeHandler = null;
            }
        }

        public bool HasActiveHandler => activeHandler != null;
    }
}
