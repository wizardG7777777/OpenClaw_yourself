using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCP.Core;
using MCP.Gateway;

namespace MCP.Executor
{
    public class TalkToNpcHandler : IActionHandler
    {
        private ActionInstance action;
        private NpcController _npc;
        private bool _cancelled;

        public bool IsComplete => action != null && action.Status != ActionStatus.Running;

        public void StartAction(ActionInstance action)
        {
            this.action = action;
            _cancelled = false;
            _npc = null;

            if (action.Target == null || action.Target.EntityObject == null)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TARGET_NOT_FOUND;
                action.Result = new { message = "NPC not found." };
                return;
            }

            // Try to get NpcController; proceed even if absent (generic NPC)
            _npc = action.Target.EntityObject.GetComponent<NpcController>();

            string characterId = _npc != null ? _npc.characterId : action.Target.EntityId;
            string npcName = _npc != null ? _npc.displayName : (action.Target.EntityId ?? "Unknown NPC");

            // Look for MCPGateway in the scene
            var gateway = Object.FindAnyObjectByType<MCPGateway>();

            if (gateway == null || !gateway.IsBackendConnected)
            {
                // Fallback: complete immediately with placeholder dialogue
                Debug.Log("[TalkToNpcHandler] Backend not connected, using fallback dialogue.");
                Debug.Log($"[MCP] Talking to NPC: {npcName}");
                action.Status = ActionStatus.Completed;
                action.Result = new { message = $"Initiated dialogue with {npcName}." };
                return;
            }

            // Extract optional topic from action args
            string topic = null;
            if (action.Result is Dictionary<string, object> args)
            {
                if (args.TryGetValue("topic", out var t))
                    topic = t?.ToString();
                else if (args.TryGetValue("dialogue_option", out var d))
                    topic = d?.ToString();
            }

            // Build request params
            var reqParams = new JObject
            {
                ["character_id"] = characterId
            };
            if (topic != null)
                reqParams["topic"] = topic;

            Debug.Log($"[TalkToNpcHandler] Sending dialogue request for '{npcName}' (id={characterId}, topic={topic ?? "none"})");

            // Set NPC into talking state
            if (_npc != null)
                _npc.SetTalking(true);

            action.Status = ActionStatus.Running;

            gateway.SendToBackend("talk_to_character", reqParams, OnDialogueResponse, 20f);
        }

        private void OnDialogueResponse(bool ok, JObject data)
        {
            if (_cancelled)
                return;

            if (action.Status != ActionStatus.Running)
                return;

            if (ok)
            {
                string reply = data?["reply"]?.ToString();
                string emotion = data?["emotion"]?.ToString();

                action.Status = ActionStatus.Completed;
                action.Result = new { reply, emotion };

                if (_npc != null)
                    _npc.SetTalking(false);

                Debug.Log($"[TalkToNpcHandler] Dialogue reply: {reply}");
            }
            else
            {
                string message = data?["message"]?.ToString() ?? "Dialogue generation failed";

                action.Status = ActionStatus.Failed;
                action.ErrorCode = "DIALOGUE_FAILED";
                action.Result = new { message };

                if (_npc != null)
                    _npc.SetTalking(false);

                Debug.LogWarning($"[TalkToNpcHandler] Dialogue failed: {message}");
            }
        }

        public void UpdateAction()
        {
            // Waiting for async callback; timeout handled by MCPRouter.
        }

        public void Cancel()
        {
            _cancelled = true;

            if (_npc != null)
                _npc.SetTalking(false);

            if (action != null)
                action.Status = ActionStatus.Cancelled;
        }
    }
}
