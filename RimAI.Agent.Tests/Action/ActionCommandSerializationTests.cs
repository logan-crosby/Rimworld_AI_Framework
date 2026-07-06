using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using RimAI.Agent.Action;
using Xunit;

namespace RimAI.Agent.Tests.Action
{
    // Spec: docs/plan/03-action.md §13.1 — round-trip commands through JSON;
    // Parameters must survive deserialization with usable values.
    public class ActionCommandSerializationTests
    {
        [Fact]
        public void RoundTrip_PreservesCoreFieldsAndParameters()
        {
            var original = new ActionCommand
            {
                Type = ActionCommandType.SetWorkPriority,
                SourceTier = "tactical",
                ConversationId = "conv-1",
                Priority = 2,
                Reason = "Cook is starving",
                Parameters = new Dictionary<string, object>
                {
                    ["colonist_id"] = "Human12345",
                    ["work_type"] = "Cooking",
                    ["priority"] = 1,
                },
            };

            var json = JsonConvert.SerializeObject(original);
            var restored = JsonConvert.DeserializeObject<ActionCommand>(json);

            Assert.Equal(original.CommandId, restored.CommandId);
            Assert.Equal(ActionCommandType.SetWorkPriority, restored.Type);
            Assert.Equal("tactical", restored.SourceTier);
            Assert.Equal("Human12345", restored.Parameters["colonist_id"].ToString());
            Assert.Equal("Cooking", restored.Parameters["work_type"].ToString());
            Assert.Equal(1, Convert.ToInt32(restored.Parameters["priority"]));
        }

        [Fact]
        public void EveryCommandType_RoundTripsThroughJson()
        {
            foreach (ActionCommandType type in Enum.GetValues(typeof(ActionCommandType)))
            {
                var cmd = new ActionCommand { Type = type };
                var restored = JsonConvert.DeserializeObject<ActionCommand>(JsonConvert.SerializeObject(cmd));
                Assert.Equal(type, restored.Type);
            }
        }

        [Fact]
        public void CommandId_IsUniquePerInstance()
        {
            var a = new ActionCommand();
            var b = new ActionCommand();
            Assert.NotEqual(a.CommandId, b.CommandId);
        }
    }
}
