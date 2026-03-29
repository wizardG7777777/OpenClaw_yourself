using System.Collections.Generic;
using UnityEngine;
using MCP.Core;
using Newtonsoft.Json.Linq;

namespace MCP.Executor
{
    /// <summary>
    /// 处理 talk_to_npc 动作：通过 BackendBridge 调用 Python 后端对话引擎，
    /// 获取 LLM 生成的角色回复。若后端不可用，回退到占位逻辑。
    /// </summary>
    public class TalkToNpcHandler : IActionHandler
    {
        private ActionInstance action;
        private bool _waitingForReply;

        public bool IsComplete => action != null && action.Status != ActionStatus.Running;

        public void StartAction(ActionInstance action)
        {
            this.action = action;

            if (action.Target == null || action.Target.EntityObject == null)
            {
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.TARGET_NOT_FOUND;
                action.Result = new { message = "NPC not found." };
                return;
            }

            string npcName = action.Target.EntityId ?? "Unknown NPC";
            Debug.Log($"[MCP] Talking to NPC: {npcName}");

            string dialogueOption = null;
            if (action.Result is Dictionary<string, object> args && args.ContainsKey("dialogue_option"))
                dialogueOption = args["dialogue_option"]?.ToString();

            if (BackendBridge.Instance != null && BackendBridge.Instance.IsConnected)
            {
                _waitingForReply = true;
                action.Status = ActionStatus.Running;
                RequestDialogue(npcName, dialogueOption ?? "你好");
            }
            else
            {
                action.Status = ActionStatus.Completed;
                action.Result = new { message = $"Initiated dialogue with {npcName}." };
            }
        }

        private async void RequestDialogue(string characterId, string message)
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "character_id", characterId },
                    { "message", message }
                };

                JObject result = await BackendBridge.Instance.SendRequest("talk_to_character", parameters);

                if (result != null)
                {
                    string reply = result.Value<string>("reply") ?? "";
                    string emotion = result.Value<string>("emotion") ?? "neutral";

                    Debug.Log($"[MCP] 角色回复 ({emotion}): {reply}");
                    action.Status = ActionStatus.Completed;
                    action.Result = new { message = reply, emotion = emotion };
                }
                else
                {
                    action.Status = ActionStatus.Completed;
                    action.Result = new { message = $"Initiated dialogue with {characterId}." };
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MCP] 对话请求失败: {ex.Message}");
                action.Status = ActionStatus.Failed;
                action.ErrorCode = "DIALOGUE_ERROR";
                action.Result = new { message = ex.Message };
            }
            finally
            {
                _waitingForReply = false;
            }
        }

        public void UpdateAction()
        {
            if (action == null || action.Status != ActionStatus.Running)
                return;

            if (Time.time - action.CreatedAt > action.Timeout)
            {
                _waitingForReply = false;
                action.Status = ActionStatus.Failed;
                action.ErrorCode = ErrorCodes.ACTION_TIMEOUT;
            }
        }

        public void Cancel()
        {
            _waitingForReply = false;
            if (action != null)
                action.Status = ActionStatus.Cancelled;
        }
    }
}
