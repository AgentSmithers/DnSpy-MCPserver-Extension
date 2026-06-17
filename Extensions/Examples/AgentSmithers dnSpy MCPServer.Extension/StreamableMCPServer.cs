using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Documents.TreeView;

// ============================================================================
//  MCP Server (revision 2025-11-25) — Streamable HTTP + stdio
//
//  Single-file merge of: McpCore.cs, McpRegistries.cs, McpDispatcher.cs,
//  McpTransports.cs, McpServer.cs. Behaviour is unchanged from the originals.
// ============================================================================

namespace Example1.Extension {
	// ════════════════════════════════════════════════════════════════════════
	//  McpCore.cs — Server identity, JSON-RPC + MCP DTOs, attributes, call context
	// ════════════════════════════════════════════════════════════════════════

	// ───────────────────────────── Server identity / versioning ─────────────────────────────
	public static class McpServerInfo {
		/// <summary>Latest stable MCP revision implemented by this server.</summary>
		public const string ProtocolVersion = "2025-11-25";

		/// <summary>
		/// Versions we will accept during initialize negotiation. We echo the client's
		/// requested version if it is in this set, otherwise we fall back to the latest.
		/// </summary>
		public static readonly IReadOnlyList<string> SupportedVersions =
			new[] { "2025-11-25", "2025-06-18", "2025-03-26" };
	}

	/// <summary>Single shared, immutable serializer configuration (thread-safe once built).</summary>
	internal static class JsonOpts {
		public static readonly JsonSerializerOptions Default = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			WriteIndented = false
		};
	}

	// ───────────────────────────── JSON-RPC 2.0 ─────────────────────────────

	public sealed class JsonRpcRequest {
		[JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";

		// Kept as a raw element so the id is echoed back with its original type (number vs string).
		// An absent id marks a JSON-RPC notification (no response is ever produced).
		[JsonPropertyName("id")] public JsonElement? Id { get; set; }

		[JsonPropertyName("method")] public string? Method { get; set; }

		[JsonPropertyName("params")] public JsonElement? Params { get; set; }

		[JsonIgnore] public bool IsNotification => Id is null;
	}

	public sealed class JsonRpcResponse {
		[JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";

		[JsonPropertyName("id")] public object? Id { get; set; }

		[JsonPropertyName("result")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public object? Result { get; set; }

		[JsonPropertyName("error")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public JsonRpcError? Error { get; set; }

		public static JsonRpcResponse Ok(object? id, object? result) =>
			new JsonRpcResponse { Id = id, Result = result };

		public static JsonRpcResponse Fail(object? id, int code, string message, object? data = null) =>
			new JsonRpcResponse { Id = id, Error = new JsonRpcError { Code = code, Message = message, Data = data } };
	}

	public sealed class JsonRpcError {
		[JsonPropertyName("code")] public int Code { get; set; }
		[JsonPropertyName("message")] public string Message { get; set; } = "";

		[JsonPropertyName("data")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public object? Data { get; set; }
	}

	public static class JsonRpcErrorCodes {
		public const int ParseError = -32700;
		public const int InvalidRequest = -32600;
		public const int MethodNotFound = -32601;
		public const int InvalidParams = -32602;
		public const int InternalError = -32603;
		public const int ResourceNotFound = -32002; // MCP convention
	}

	// ───────────────────────────── MCP result/capability DTOs ─────────────────────────────

	public sealed class InitializeResult {
		[JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = McpServerInfo.ProtocolVersion;
		[JsonPropertyName("capabilities")] public ServerCapabilities Capabilities { get; set; } = new ServerCapabilities();
		[JsonPropertyName("serverInfo")] public Implementation ServerInfo { get; set; } = new Implementation();

		[JsonPropertyName("instructions")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Instructions { get; set; }
	}

	public sealed class ServerCapabilities {
		[JsonPropertyName("tools")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public ListChangedCapability? Tools { get; set; }

		[JsonPropertyName("prompts")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public ListChangedCapability? Prompts { get; set; }

		[JsonPropertyName("resources")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public ResourcesCapability? Resources { get; set; }
	}

	public sealed class ListChangedCapability {
		[JsonPropertyName("listChanged")] public bool ListChanged { get; set; }
	}

	public sealed class ResourcesCapability {
		[JsonPropertyName("subscribe")] public bool Subscribe { get; set; }
		[JsonPropertyName("listChanged")] public bool ListChanged { get; set; }
	}

	public sealed class Implementation {
		[JsonPropertyName("name")] public string Name { get; set; } = "";
		[JsonPropertyName("version")] public string Version { get; set; } = "";
	}

	public sealed class Tool {
		[JsonPropertyName("name")] public string Name { get; set; } = "";

		[JsonPropertyName("description")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Description { get; set; }

		[JsonPropertyName("inputSchema")] public object InputSchema { get; set; } = new object();
	}

	// ───────────────────────────── Attributes & ambient context ─────────────────────────────

	/// <summary>Marks a static method as an MCP tool. Drop-in compatible with the original attribute.</summary>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
	public sealed class CommandAttribute : Attribute {
		public string? Name { get; }
		public bool DebugOnly { get; set; }
		public string? MCPCmdDescription { get; set; }

		public CommandAttribute() { }
		public CommandAttribute(string name) { Name = name; }
	}

	/// <summary>Optional per-parameter description carried into the generated JSON schema.</summary>
	[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
	public sealed class McpParamAttribute : Attribute {
		public string Description { get; }
		public McpParamAttribute(string description) { Description = description; }
	}

	/// <summary>
	/// Transport-independent per-call context. Replaces reaching into HttpContext.Current from
	/// business logic, so the same tool code runs unchanged under HTTP and stdio.
	/// </summary>
	public sealed class McpCallContext {
		private static readonly AsyncLocal<McpCallContext?> _current = new AsyncLocal<McpCallContext?>();

		public static McpCallContext? Current {
			get => _current.Value;
			set => _current.Value = value;
		}

		public string? TransportKind { get; set; } // "http" | "stdio"
		public string? SessionId { get; set; }
		public string? AccessLevel { get; set; }   // e.g. the old HTTP_ACCESS header value
	}

	// ════════════════════════════════════════════════════════════════════════
	//  McpRegistries.cs — Tool / Prompt / Resource registries
	// ════════════════════════════════════════════════════════════════════════

	// ───────────────────────────── Tools ─────────────────────────────

	public sealed class ToolEntry {
		public MethodInfo Method { get; }
		public Tool Descriptor { get; }
		public bool DebugOnly { get; }

		public ToolEntry(MethodInfo method, Tool descriptor, bool debugOnly) {
			Method = method;
			Descriptor = descriptor;
			DebugOnly = debugOnly;
		}
	}

	/// <summary>
	/// Built once at startup via reflection, then immutable. No locking is required on the read
	/// path because the backing dictionary is never mutated afterward.
	/// </summary>
	public sealed class ToolRegistry {
		private static readonly Regex NamePattern = new Regex("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);

		private readonly IReadOnlyDictionary<string, ToolEntry> _tools;
		private readonly IReadOnlyList<Tool> _wire;

		private ToolRegistry(Dictionary<string, ToolEntry> tools) {
			_tools = tools;
			_wire = tools.Values.Select(t => t.Descriptor).ToArray();
		}

		public IReadOnlyList<Tool> List() => _wire;

		public bool TryGet(string name, out ToolEntry entry) {
			if (_tools.TryGetValue(name, out var found)) {
				entry = found;
				return true;
			}
			entry = null!;
			return false;
		}

		public static ToolRegistry Build(Type source) {
			var map = new Dictionary<string, ToolEntry>(StringComparer.Ordinal);
			const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

			foreach (var method in source.GetMethods(flags)) {
				var attr = method.GetCustomAttribute<CommandAttribute>();
				if (attr is null) continue;

				var name = attr.Name ?? method.Name;
				if (!NamePattern.IsMatch(name) || map.ContainsKey(name)) continue;

				var descriptor = new Tool {
					Name = name,
					Description = string.IsNullOrEmpty(attr.MCPCmdDescription)
						? $"Executes the {name} command."
						: attr.MCPCmdDescription,
					InputSchema = BuildInputSchema(method, name)
				};

				map[name] = new ToolEntry(method, descriptor, attr.DebugOnly);
			}

			return new ToolRegistry(map);
		}

		/// <summary>
		/// Binds JSON arguments to the method signature and invokes it. Awaits Task / Task&lt;T&gt;
		/// so I/O-bound tools do not block a thread-pool thread. A CancellationToken parameter,
		/// if present, is supplied automatically and excluded from the public schema.
		/// </summary>
		public async Task<string> InvokeAsync(ToolEntry entry, JsonElement? args, CancellationToken ct) {
			var parameters = entry.Method.GetParameters();
			var bound = new object?[parameters.Length];

			for (int i = 0; i < parameters.Length; i++) {
				var p = parameters[i];

				if (p.ParameterType == typeof(CancellationToken)) {
					bound[i] = ct;
					continue;
				}

				if (args is JsonElement a && a.ValueKind == JsonValueKind.Object &&
					a.TryGetProperty(p.Name!, out var raw)) {
					bound[i] = ConvertArgument(raw, p.ParameterType, p.Name!);
				}
				else if (p.IsOptional) {
					bound[i] = p.DefaultValue;
				}
				else {
					throw new ArgumentException($"Missing required argument: '{p.Name}'.");
				}
			}

			var result = entry.Method.Invoke(null, bound);
			result = await UnwrapAsync(result).ConfigureAwait(false);
			return result?.ToString() ?? $"{entry.Descriptor.Name} executed successfully.";
		}

		private static async Task<object?> UnwrapAsync(object? result) {
			if (result is Task task) {
				await task.ConfigureAwait(false);
				var resultProp = task.GetType().GetProperty("Result");
				return resultProp != null && resultProp.PropertyType.Name != "VoidTaskResult"
					? resultProp.GetValue(task)
					: null;
			}
			return result;
		}

		private static object? ConvertArgument(JsonElement value, Type targetType, string paramName) {
			if (value.ValueKind == JsonValueKind.Null) {
				if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null) return null;
				throw new ArgumentException($"Null provided for non-nullable parameter '{paramName}'.");
			}

			try {
				return JsonSerializer.Deserialize(value.GetRawText(), targetType, JsonOpts.Default);
			}
			catch (Exception ex) {
				throw new ArgumentException(
					$"Cannot convert argument '{paramName}' to {targetType.Name}: {ex.Message}", ex);
			}
		}

		// ── JSON Schema generation ──

		private static object BuildInputSchema(MethodInfo method, string toolName) {
			var properties = new Dictionary<string, object>();
			var required = new List<string>();

			foreach (var p in method.GetParameters()) {
				if (p.ParameterType == typeof(CancellationToken)) continue;
				properties[p.Name!] = BuildParamSchema(p, toolName);
				if (!p.IsOptional) required.Add(p.Name!);
			}

			return new Dictionary<string, object?> {
				["type"] = "object",
				["title"] = toolName,
				["properties"] = properties,
				["required"] = required.ToArray()
			};
		}

		private static object BuildParamSchema(ParameterInfo p, string toolName) {
			var description = p.GetCustomAttribute<McpParamAttribute>()?.Description
							  ?? $"Parameter '{p.Name}' for {toolName}.";

			if (p.ParameterType.IsArray) {
				var elementType = p.ParameterType.GetElementType();
				return new Dictionary<string, object?> {
					["type"] = "array",
					["description"] = description,
					["items"] = new Dictionary<string, object?> {
						["type"] = elementType != null ? JsonSchemaType(elementType) : "string"
					}
				};
			}

			return new Dictionary<string, object?> {
				["type"] = JsonSchemaType(p.ParameterType),
				["description"] = description
			};
		}

		private static string JsonSchemaType(Type type) {
			type = Nullable.GetUnderlyingType(type) ?? type;

			if (type == typeof(string) || type == typeof(Guid) || type.IsEnum) return "string";
			if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
				type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
				return "integer";
			if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
			if (type == typeof(bool)) return "boolean";
			if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return "array";
			return "object";
		}
	}

	// ───────────────────────────── Prompts ─────────────────────────────

	public sealed class PromptArgument {
		public string Name { get; set; } = "";
		public string? Description { get; set; }
		public bool Required { get; set; }
	}

	public sealed class PromptMessageTemplate {
		public string Role { get; set; } = "user"; // "user" | "assistant"
		public string Type { get; set; } = "text";
		public string Text { get; set; } = "";     // may contain {argName} placeholders
	}

	public sealed class PromptDefinition {
		public string Name { get; set; } = "";
		public string? Description { get; set; }
		public List<PromptArgument> Arguments { get; set; } = new List<PromptArgument>();
		public List<PromptMessageTemplate> MessageTemplates { get; set; } = new List<PromptMessageTemplate>();
	}

	public sealed class PromptRegistry {
		private readonly IReadOnlyList<PromptDefinition> _prompts;

		public PromptRegistry(IEnumerable<PromptDefinition> prompts) => _prompts = prompts.ToArray();

		public bool IsEmpty => _prompts.Count == 0;

		/// <summary>prompts/list wire shape — note message templates are intentionally NOT exposed.</summary>
		public object[] List() => _prompts.Select(p => (object)new {
			name = p.Name,
			description = p.Description,
			arguments = p.Arguments.Select(a => new {
				name = a.Name,
				description = a.Description,
				required = a.Required
			}).ToArray()
		}).ToArray();

		public PromptDefinition? Find(string name) =>
			_prompts.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
	}

	// ───────────────────────────── Resources ─────────────────────────────

	public sealed class ResourceEntry {
		public string Uri { get; set; } = "";
		public string Name { get; set; } = "";
		public string? Description { get; set; }
		public string? MimeType { get; set; }
		public string? Text { get; set; } // inline content for resources/read (optional)
	}

	public sealed class ResourceTemplateEntry {
		public string UriTemplate { get; set; } = "";
		public string Name { get; set; } = "";
		public string? Description { get; set; }
		public string? MimeType { get; set; }
	}

	public sealed class ResourceRegistry {
		private readonly IReadOnlyDictionary<string, ResourceEntry> _resources;
		private readonly IReadOnlyList<ResourceTemplateEntry> _templates;

		public ResourceRegistry(IEnumerable<ResourceEntry> resources, IEnumerable<ResourceTemplateEntry> templates) {
			_resources = resources.ToDictionary(r => r.Uri, StringComparer.OrdinalIgnoreCase);
			_templates = templates.ToArray();
		}

		public bool IsEmpty => _resources.Count == 0 && _templates.Count == 0;

		public object[] ListResources() => _resources.Values.Select(r => (object)new {
			uri = r.Uri,
			name = r.Name,
			description = r.Description,
			mimeType = r.MimeType
		}).ToArray();

		public object[] ListTemplates() => _templates.Select(t => (object)new {
			uriTemplate = t.UriTemplate,
			name = t.Name,
			description = t.Description,
			mimeType = t.MimeType
		}).ToArray();

		public bool TryRead(string uri, out ResourceEntry entry) {
			if (_resources.TryGetValue(uri, out var found)) {
				entry = found;
				return true;
			}
			entry = null!;
			return false;
		}
	}

	// ════════════════════════════════════════════════════════════════════════
	//  McpDispatcher.cs — Transport-agnostic protocol core
	// ════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// The transport-agnostic heart of the server. It consumes a parsed JSON-RPC request and
	/// returns a JSON-RPC response (or null for notifications). It has no knowledge of HTTP,
	/// stdio, sessions, or sockets — both transports call straight into HandleAsync.
	/// </summary>
	public sealed class McpDispatcher {
		private readonly ToolRegistry _tools;
		private readonly PromptRegistry _prompts;
		private readonly ResourceRegistry _resources;
		private readonly Implementation _serverInfo;
		private readonly string? _instructions;

		public McpDispatcher(
			ToolRegistry tools,
			PromptRegistry prompts,
			ResourceRegistry resources,
			Implementation serverInfo,
			string? instructions = null) {
			_tools = tools;
			_prompts = prompts;
			_resources = resources;
			_serverInfo = serverInfo;
			_instructions = instructions;
		}

		public async Task<JsonRpcResponse?> HandleAsync(JsonRpcRequest? req, CancellationToken ct) {
			if (req is null || string.IsNullOrEmpty(req.Method))
				return JsonRpcResponse.Fail(null, JsonRpcErrorCodes.InvalidRequest, "Invalid Request: missing method.");

			// Notifications (no id) never receive a response, even on error.
			if (req.IsNotification)
				return null;

			object? id = req.Id.HasValue ? (object)req.Id.Value : null;

			try {
				switch (req.Method) {
				case "initialize":
					return JsonRpcResponse.Ok(id, BuildInitialize(req.Params));

				case "ping":
					return JsonRpcResponse.Ok(id, new { });

				case "tools/list":
					return JsonRpcResponse.Ok(id, new { tools = _tools.List() });

				case "tools/call":
					return await HandleToolCallAsync(id, req.Params, ct).ConfigureAwait(false);

				case "prompts/list":
					return JsonRpcResponse.Ok(id, new { prompts = _prompts.List() });

				case "prompts/get":
					return HandlePromptGet(id, req.Params);

				case "resources/list":
					return JsonRpcResponse.Ok(id, new { resources = _resources.ListResources() });

				case "resources/templates/list":
					return JsonRpcResponse.Ok(id, new { resourceTemplates = _resources.ListTemplates() });

				case "resources/read":
					return HandleResourceRead(id, req.Params);

				default:
					return JsonRpcResponse.Fail(id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {req.Method}");
				}
			}
			catch (Exception ex) {
				// The protocol layer must never throw out to the transport.
				return JsonRpcResponse.Fail(id, JsonRpcErrorCodes.InternalError, $"Internal error: {ex.Message}");
			}
		}

		// ── initialize ──

		private InitializeResult BuildInitialize(JsonElement? @params) {
			var negotiated = McpServerInfo.ProtocolVersion;
			if (@params is JsonElement p && p.ValueKind == JsonValueKind.Object &&
				p.TryGetProperty("protocolVersion", out var v) && v.ValueKind == JsonValueKind.String) {
				var requested = v.GetString();
				if (requested != null && McpServerInfo.SupportedVersions.Contains(requested))
					negotiated = requested;
			}

			var caps = new ServerCapabilities {
				Tools = new ListChangedCapability { ListChanged = false }
			};
			if (!_prompts.IsEmpty) caps.Prompts = new ListChangedCapability { ListChanged = false };
			if (!_resources.IsEmpty) caps.Resources = new ResourcesCapability { Subscribe = false, ListChanged = false };

			return new InitializeResult {
				ProtocolVersion = negotiated,
				Capabilities = caps,
				ServerInfo = _serverInfo,
				Instructions = _instructions
			};
		}

		// ── tools/call ──

		private async Task<JsonRpcResponse> HandleToolCallAsync(object? id, JsonElement? @params, CancellationToken ct) {
			if (@params is not JsonElement p || p.ValueKind != JsonValueKind.Object)
				return JsonRpcResponse.Fail(id, JsonRpcErrorCodes.InvalidParams, "Invalid params for tools/call.");

			if (!p.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
				return JsonRpcResponse.Fail(id, JsonRpcErrorCodes.InvalidParams, "Missing or invalid tool name.");

			var toolName = nameEl.GetString()!;
			JsonElement? args = p.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object
				? argsEl
				: (JsonElement?)null;

			// Built-in echo (kept for parity with the original).
			if (string.Equals(toolName, "Echo", StringComparison.OrdinalIgnoreCase)) {
				var msg = args is JsonElement ea && ea.TryGetProperty("message", out var m) ? m.ToString() : "";
				return ToolResult(id, $"Echo response: {msg}", isError: false);
			}

			if (!_tools.TryGet(toolName, out var entry))
				return ToolResult(id, $"Tool '{toolName}' not found.", isError: true);

			// Per MCP, a tool *execution* failure is reported via isError in the result,
			// not as a JSON-RPC protocol error.
			try {
				var text = await _tools.InvokeAsync(entry, args, ct).ConfigureAwait(false);
				return ToolResult(id, text, isError: false);
			}
			catch (System.Reflection.TargetInvocationException tie) {
				return ToolResult(id, $"Error executing '{toolName}': {tie.InnerException?.Message ?? tie.Message}", isError: true);
			}
			catch (Exception ex) {
				return ToolResult(id, $"Error processing '{toolName}': {ex.Message}", isError: true);
			}
		}

		private static JsonRpcResponse ToolResult(object? id, string text, bool isError) =>
			JsonRpcResponse.Ok(id, new {
				content = new[] { new { type = "text", text } },
				isError
			});

		// ── prompts/get ──

		private JsonRpcResponse HandlePromptGet(object? id, JsonElement? @params) {
			if (@params is not JsonElement p || p.ValueKind != JsonValueKind.Object ||
				!p.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
				return JsonRpcResponse.Fail(id, JsonRpcErrorCodes.InvalidParams, "Missing or invalid prompt name.");

			var promptName = nameEl.GetString()!;
			var prompt = _prompts.Find(promptName);
			if (prompt is null)
				return JsonRpcResponse.Fail(id, JsonRpcErrorCodes.MethodNotFound, $"Prompt not found: {promptName}");

			var args = p.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object
				? argsEl
				: (JsonElement?)null;

			// Validate required arguments.
			foreach (var required in prompt.Arguments.Where(a => a.Required)) {
				var present = args is JsonElement ae && ae.TryGetProperty(required.Name, out _);
				if (!present)
					return JsonRpcResponse.Fail(id, JsonRpcErrorCodes.InvalidParams,
						$"Missing required argument '{required.Name}' for prompt '{promptName}'.");
			}

			var messages = new List<object>();
			foreach (var template in prompt.MessageTemplates) {
				var text = template.Text ?? "";
				foreach (var argDef in prompt.Arguments) {
					var placeholder = "{" + argDef.Name + "}";
					if (!text.Contains(placeholder)) continue;

					var value = "";
					if (args is JsonElement ae2 && ae2.TryGetProperty(argDef.Name, out var argVal) &&
						argVal.ValueKind != JsonValueKind.Null)
						value = argVal.ToString();

					text = text.Replace(placeholder, value);
				}

				messages.Add(new {
					role = template.Role,
					content = new { type = template.Type, text }
				});
			}

			return JsonRpcResponse.Ok(id, new { description = prompt.Description, messages });
		}

		// ── resources/read ──

		private JsonRpcResponse HandleResourceRead(object? id, JsonElement? @params) {
			if (@params is not JsonElement p || p.ValueKind != JsonValueKind.Object ||
				!p.TryGetProperty("uri", out var uriEl) || uriEl.ValueKind != JsonValueKind.String)
				return JsonRpcResponse.Fail(id, JsonRpcErrorCodes.InvalidParams, "Missing 'uri' for resources/read.");

			var uri = uriEl.GetString()!;
			if (!_resources.TryRead(uri, out var entry) || entry.Text is null)
				return JsonRpcResponse.Fail(id, JsonRpcErrorCodes.ResourceNotFound, $"Resource not found or not readable: {uri}");

			return JsonRpcResponse.Ok(id, new {
				contents = new[]
				{
					new { uri = entry.Uri, mimeType = entry.MimeType, text = entry.Text }
				}
			});
		}
	}

	// ════════════════════════════════════════════════════════════════════════
	//  McpTransports.cs — stdio + Streamable HTTP transports
	// ════════════════════════════════════════════════════════════════════════

	// ───────────────────────────── stdio (request/response over pipes) ─────────────────────────────

	/// <summary>
	/// The non-streamable standard transport. Reads newline-delimited JSON-RPC from stdin and writes
	/// responses to stdout. stdout carries ONLY MCP messages — all logging must go to stderr.
	/// </summary>
	public sealed class StdioTransport {
		private readonly McpDispatcher _dispatcher;

		public StdioTransport(McpDispatcher dispatcher) => _dispatcher = dispatcher;

		public async Task RunAsync(CancellationToken ct = default) {
			using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
			using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

			string? line;
			while (!ct.IsCancellationRequested && (line = await stdin.ReadLineAsync().ConfigureAwait(false)) != null) {
				if (line.Length == 0) continue;

				McpCallContext.Current = new McpCallContext { TransportKind = "stdio", AccessLevel = "LOCAL" };

				JsonRpcRequest? rpc;
				try {
					rpc = JsonSerializer.Deserialize<JsonRpcRequest>(line, JsonOpts.Default);
				}
				catch (Exception ex) {
					await WriteAsync(stdout, JsonRpcResponse.Fail(null, JsonRpcErrorCodes.ParseError, $"Parse error: {ex.Message}"))
						.ConfigureAwait(false);
					continue;
				}

				var resp = await _dispatcher.HandleAsync(rpc, ct).ConfigureAwait(false);
				if (resp != null)
					await WriteAsync(stdout, resp).ConfigureAwait(false);
			}
		}

		private static Task WriteAsync(StreamWriter writer, JsonRpcResponse resp) {
			// One compact line, newline-delimited, no embedded newlines.
			var json = JsonSerializer.Serialize(resp, JsonOpts.Default);
			return writer.WriteLineAsync(json);
		}
	}

	// ───────────────────────────── Streamable HTTP (single endpoint) ─────────────────────────────

	/// <summary>
	/// The streamable standard transport (spec 2025-11-25). One endpoint (default /mcp) handles:
	///   POST  — a JSON-RPC request returns its response as application/json; a notification/response → 202.
	///   GET   — opens an optional SSE stream for server→client messages (405 if Accept lacks event-stream).
	///   DELETE— terminates a session.
	/// Origin is validated to defend against DNS rebinding; sessions are tracked via MCP-Session-Id.
	/// </summary>
	public sealed class StreamableHttpTransport : IDisposable {
		private readonly HttpListener _listener = new HttpListener();
		private readonly McpDispatcher _dispatcher;
		private readonly string _endpointPath;
		private readonly System.Collections.Generic.HashSet<string> _allowedOrigins;
		private readonly ConcurrentDictionary<string, SseSession> _sessions = new ConcurrentDictionary<string, SseSession>();
		private CancellationTokenSource? _cts;

		public StreamableHttpTransport(
			McpDispatcher dispatcher,
			string host = "127.0.0.1",
			int port = 50301,
			string path = "/mcp",
			System.Collections.Generic.IEnumerable<string>? allowedOrigins = null) {
			_dispatcher = dispatcher;
			_endpointPath = "/" + path.Trim('/');
			_allowedOrigins = new System.Collections.Generic.HashSet<string>(
				allowedOrigins ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

			// Single MCP endpoint. (On Windows a urlacl reservation may be needed for non-loopback hosts.)
			_listener.Prefixes.Add($"http://{host}:{port}{_endpointPath}/");
		}

		public void Start() {
			_cts = new CancellationTokenSource();
			_listener.Start();
			Console.Error.WriteLine($"[MCP/HTTP] Listening on {string.Join(", ", _listener.Prefixes)}");
			_ = AcceptLoopAsync(_cts.Token);
		}

		public void Stop() {
			_cts?.Cancel();
			try { _listener.Stop(); } catch { /* already stopped */ }
		}

		private async Task AcceptLoopAsync(CancellationToken ct) {
			while (!ct.IsCancellationRequested) {
				HttpListenerContext ctx;
				try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
				catch (HttpListenerException) { break; }
				catch (ObjectDisposedException) { break; }

				_ = Task.Run(() => HandleAsync(ctx, ct), ct); // never block the accept loop
			}
		}

		private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct) {
			var req = ctx.Request;
			var res = ctx.Response;

			try {
				if (req.HttpMethod == "OPTIONS") {
					WriteCors(req, res);
					res.StatusCode = 204;
					res.Close();
					return;
				}

				// Security: validate Origin to prevent DNS rebinding.
				var origin = req.Headers["Origin"];
				if (!string.IsNullOrEmpty(origin) && !IsAllowedOrigin(origin!)) {
					res.StatusCode = 403;
					await WriteTextAsync(res, "Forbidden origin.").ConfigureAwait(false);
					return;
				}
				WriteCors(req, res);

				// Validate the negotiated protocol version when supplied.
				var version = req.Headers["MCP-Protocol-Version"];
				if (!string.IsNullOrEmpty(version) && !McpServerInfo.SupportedVersions.Contains(version!)) {
					res.StatusCode = 400;
					await WriteTextAsync(res, "Unsupported MCP-Protocol-Version.").ConfigureAwait(false);
					return;
				}

				var path = req.Url!.AbsolutePath.TrimEnd('/');
				if (path.Length == 0) path = "/";
				if (!string.Equals(path, _endpointPath, StringComparison.OrdinalIgnoreCase)) {
					res.StatusCode = 404;
					res.Close();
					return;
				}

				switch (req.HttpMethod) {
				case "POST": await HandlePostAsync(ctx, ct).ConfigureAwait(false); break;
				case "GET": await HandleGetAsync(ctx, ct).ConfigureAwait(false); break;
				case "DELETE": HandleDelete(ctx); break;
				default:
					res.StatusCode = 405;
					res.AddHeader("Allow", "GET, POST, DELETE");
					res.Close();
					break;
				}
			}
			catch (Exception ex) {
				Console.Error.WriteLine($"[MCP/HTTP] Unhandled: {ex}");
				try { res.StatusCode = 500; await WriteTextAsync(res, "Internal error.").ConfigureAwait(false); }
				catch { /* response already closed */ }
			}
		}

		private async Task HandlePostAsync(HttpListenerContext ctx, CancellationToken ct) {
			var req = ctx.Request;
			var res = ctx.Response;

			string body;
			using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
				body = await reader.ReadToEndAsync().ConfigureAwait(false);

			JsonRpcRequest? rpc;
			try {
				rpc = JsonSerializer.Deserialize<JsonRpcRequest>(body, JsonOpts.Default);
			}
			catch (Exception ex) {
				await WriteJsonAsync(res, 200, JsonRpcResponse.Fail(null, JsonRpcErrorCodes.ParseError, $"Parse error: {ex.Message}"))
					.ConfigureAwait(false);
				return;
			}

			if (rpc is null) {
				await WriteJsonAsync(res, 200, JsonRpcResponse.Fail(null, JsonRpcErrorCodes.InvalidRequest, "Empty request."))
					.ConfigureAwait(false);
				return;
			}

			McpCallContext.Current = new McpCallContext {
				TransportKind = "http",
				SessionId = req.Headers["MCP-Session-Id"],
				AccessLevel = req.Headers["HTTP_ACCESS"]
			};

			// A notification or response gets 202 with NO body.
			if (rpc.IsNotification) {
				await _dispatcher.HandleAsync(rpc, ct).ConfigureAwait(false);
				res.StatusCode = 202;
				res.Close();
				return;
			}

			var response = await _dispatcher.HandleAsync(rpc, ct).ConfigureAwait(false);

			// Assign a session at initialize time so the client can later open a GET stream.
			if (string.Equals(rpc.Method, "initialize", StringComparison.Ordinal)) {
				var sid = GenerateSessionId();
				_sessions[sid] = new SseSession(sid);
				res.AddHeader("MCP-Session-Id", sid);
			}

			await WriteJsonAsync(res, 200, response!).ConfigureAwait(false);
		}

		private async Task HandleGetAsync(HttpListenerContext ctx, CancellationToken ct) {
			var req = ctx.Request;
			var res = ctx.Response;

			var accept = req.Headers["Accept"] ?? "";
			if (accept.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) < 0) {
				res.StatusCode = 405; // We offer no SSE stream unless the client asks for one.
				res.AddHeader("Allow", "POST, DELETE");
				res.Close();
				return;
			}

			res.StatusCode = 200;
			res.ContentType = "text/event-stream; charset=utf-8";
			res.SendChunked = true;
			res.KeepAlive = true;
			res.Headers["Cache-Control"] = "no-cache";
			res.Headers["X-Accel-Buffering"] = "no";

			var writer = new StreamWriter(res.OutputStream, new UTF8Encoding(false)) { AutoFlush = true };

			var sessionId = req.Headers["MCP-Session-Id"];
			SseSession? session = null;
			if (!string.IsNullOrEmpty(sessionId) && _sessions.TryGetValue(sessionId!, out var s)) {
				session = s;
				session.Attach(writer);
			}

			try {
				// Keep-alive comments hold the stream open. No server→client messages are sent by default,
				// but this channel exists for notifications such as tools/list_changed.
				while (!ct.IsCancellationRequested) {
					await Task.Delay(15000, ct).ConfigureAwait(false);
					await writer.WriteAsync(": ping\n\n").ConfigureAwait(false);
				}
			}
			catch { /* client disconnected or cancelled */ }
			finally {
				session?.Detach();
				try { writer.Dispose(); } catch { }
				try { res.Close(); } catch { }
			}
		}

		private void HandleDelete(HttpListenerContext ctx) {
			var sid = ctx.Request.Headers["MCP-Session-Id"];
			if (!string.IsNullOrEmpty(sid) && _sessions.TryRemove(sid!, out var s))
				s.Dispose();

			ctx.Response.StatusCode = 200;
			ctx.Response.Close();
		}

		// ── helpers ──

		private bool IsAllowedOrigin(string origin) {
			if (_allowedOrigins.Count > 0)
				return _allowedOrigins.Contains(origin);

			// Secure default: permit only loopback origins.
			return Uri.TryCreate(origin, UriKind.Absolute, out var u) && u.IsLoopback;
		}

		private static void WriteCors(HttpListenerRequest req, HttpListenerResponse res) {
			var origin = req.Headers["Origin"];
			if (!string.IsNullOrEmpty(origin))
				res.Headers["Access-Control-Allow-Origin"] = origin;

			res.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
			res.Headers["Access-Control-Allow-Headers"] =
				"Content-Type, Authorization, MCP-Session-Id, MCP-Protocol-Version, Last-Event-ID";
			res.Headers["Access-Control-Expose-Headers"] = "MCP-Session-Id";
		}

		private static async Task WriteJsonAsync(HttpListenerResponse res, int status, JsonRpcResponse payload) {
			var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts.Default);
			res.StatusCode = status;
			res.ContentType = "application/json; charset=utf-8";
			res.ContentLength64 = bytes.Length;
			await res.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
			res.Close();
		}

		private static async Task WriteTextAsync(HttpListenerResponse res, string text) {
			var bytes = Encoding.UTF8.GetBytes(text);
			res.ContentType = "text/plain; charset=utf-8";
			res.ContentLength64 = bytes.Length;
			await res.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
			res.Close();
		}

		private static string GenerateSessionId() {
			var bytes = new byte[16];
			using (var rng = RandomNumberGenerator.Create())
				rng.GetBytes(bytes);

			// Base64Url → visible ASCII only (0x21–0x7E), as the spec requires for session IDs.
			return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		public void Dispose() {
			Stop();
			foreach (var s in _sessions.Values) s.Dispose();
			_sessions.Clear();
			try { _listener.Close(); } catch { }
			_cts?.Dispose();
		}
	}

	/// <summary>
	/// Tracks an optional server→client SSE stream for a session. A SemaphoreSlim serializes writes
	/// so concurrent server-initiated messages cannot interleave on the wire.
	/// </summary>
	internal sealed class SseSession : IDisposable {
		private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
		private StreamWriter? _writer;

		public string Id { get; }

		public SseSession(string id) => Id = id;

		public void Attach(StreamWriter writer) => _writer = writer;
		public void Detach() => _writer = null;

		public async Task SendAsync(string jsonRpcMessage) {
			var writer = _writer;
			if (writer is null) return;

			await _writeLock.WaitAsync().ConfigureAwait(false);
			try {
				await writer.WriteAsync($"data: {jsonRpcMessage}\n\n").ConfigureAwait(false);
				await writer.FlushAsync().ConfigureAwait(false);
			}
			finally {
				_writeLock.Release();
			}
		}

		public void Dispose() {
			try { _writer?.Dispose(); } catch { }
			_writeLock.Dispose();
		}
	}

	// ════════════════════════════════════════════════════════════════════════
	//  McpServer.cs — Composition root
	// ════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// Drop-in replacement for the old SimpleMcpServer. Builds the shared registry + dispatcher once
	/// and exposes both standard transports. Wire it up exactly like the original:
	///
	///     // HTTP (streamable):
	///     Global.MyServer = new McpServer(typeof(CommandImplementations));
	///     Global.MyServer.StartHttp();                      // http://127.0.0.1:50301/mcp
	///
	///     // stdio (non-streamable) — e.g. when launched as a child process by an MCP client:
	///     await new McpServer(typeof(CommandImplementations)).RunStdioAsync();
	///
	/// Both transports route through the identical dispatcher, so tool/prompt/resource logic is shared.
	/// Your existing [Command]-decorated static methods need no changes beyond `using Example1.Extension.Mcp;`.
	/// </summary>
	public sealed class McpServer {
		public McpDispatcher Dispatcher { get; }

		private StreamableHttpTransport? _http;

		public McpServer(
			Type commandSource,
			IEnumerable<PromptDefinition>? prompts = null,
			IEnumerable<ResourceEntry>? resources = null,
			IEnumerable<ResourceTemplateEntry>? resourceTemplates = null,
			string serverName = "AgentSmithers-dnSpyExMcpServer",
			string version = "1.0.0",
			string? instructions = "Welcome to the AgentSmithers dnSpyEx MCP Server!") {
			var tools = ToolRegistry.Build(commandSource);
			var promptReg = new PromptRegistry(prompts ?? DefaultPrompts());
			var resourceReg = new ResourceRegistry(
				resources ?? DefaultResources(),
				resourceTemplates ?? DefaultResourceTemplates());

			Dispatcher = new McpDispatcher(
				tools, promptReg, resourceReg,
				new Implementation { Name = serverName, Version = version },
				instructions);
		}

		public void StartHttp(string host = "*", int port = 50301, string path = "/mcp",
							  IEnumerable<string>? allowedOrigins = null) {
			_http = new StreamableHttpTransport(Dispatcher, host, port, path, allowedOrigins);
			_http.Start();
		}

		public void StopHttp() => _http?.Stop();

		public Task RunStdioAsync(CancellationToken ct = default) =>
			new StdioTransport(Dispatcher).RunAsync(ct);

		// ── Defaults carried over from the original implementation ──

		private static IEnumerable<PromptDefinition> DefaultPrompts() => new[]
		{
			new PromptDefinition
			{
				Name = "dnSpyEx-Prompt",
				Description = "Default prompt asking the AI to use the dnSpyEx functionality.",
				MessageTemplates = new List<PromptMessageTemplate>
				{
					new PromptMessageTemplate
					{
						Role = "user",
						Type = "text",
						Text = "You are an AI assistant with access to an MCP server connected to dnSpyEx " +
							   "for decompiling .NET applications. Complete tasks by calling the available tools."
					}
				}
			}
		};

		private static IEnumerable<ResourceEntry> DefaultResources() => new[]
		{
			new ResourceEntry
			{
				Uri = "/files/config.json",
				Name = "Configuration File",
				Description = "Server-side configuration in JSON format",
				MimeType = "application/json",
				Text = "{ \"example\": true }"
			},
			new ResourceEntry
			{
				Uri = "/images/logo.png",
				Name = "Logo Image",
				Description = "Company logo",
				MimeType = "image/png"
                // No inline Text → resources/read returns ResourceNotFound for this binary entry.
            }
		};

		private static IEnumerable<ResourceTemplateEntry> DefaultResourceTemplates() => new[]
		{
			new ResourceTemplateEntry
			{
				UriTemplate = "/logs/{date}",
				Name = "Log File by Date",
				Description = "Retrieve logs for a specific date (YYYY-MM-DD)",
				MimeType = "text/plain"
			}
		};
	}

}
