using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web.Script.Serialization;
using dnSpy.Contracts.Documents.TreeView;
//https://modelcontextprotocol.io/docs/concepts/prompts
//https://prasanthmj.github.io/ai/mcp-go/

namespace Example1.Extension {

	static class Global
	{
		public static SimpleMcpServer MySimpleMCPServer;
		public static IDocumentTreeView MyTreeView;
	}
	class SimpleMcpServer {
			private readonly HttpListener _listener = new HttpListener();
			private readonly Dictionary<string, MethodInfo> _commands = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
			private readonly Type _targetType;
			public bool IsActivelyDebugging = false;
			public bool OutputPlugingDebugInformation = true;

			public SimpleMcpServer(Type commandSourceType) {
				//DisableServerHeader(); //Prob not needed
				_targetType = commandSourceType;
				string IPAddress = "+"; //127.0.0.1
				string port = "3003"; //64163
				Console.WriteLine("MCP server listening on " + IPAddress + ":" + port);

				_listener.Prefixes.Add("http://" + IPAddress + ":" + port + "/sse/"); //Request come in without a trailing '/' but are still handled
				_listener.Prefixes.Add("http://" + IPAddress + ":" + port + "/message/");
				// Reflect and register [Command] methods
				if (commandSourceType != null) {
					foreach (var method in commandSourceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
						var attr = method.GetCustomAttribute<CommandAttribute>();
						if (attr != null)
							_commands[attr.Name] = method;
					}
				}
				
			}

		[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
		public class CommandAttribute : Attribute {
			public string Name { get; }
			public bool DebugOnly { get; set; } //Command is only visual during an active debug session of a binary.
			public string MCPCmdDescription { get; set; }

			public CommandAttribute() { }

			public CommandAttribute(string name) {
				Name = name;
			}
		}

		private void OutputDebugInforamtion() {
				if (OutputPlugingDebugInformation) { }
			}

			private static void ExecuteCommand(string command) {
				var process = new Process {
					StartInfo = new ProcessStartInfo("cmd.exe", "/c " + command) {
						Verb = "runas", // Run as administrator
						CreateNoWindow = true,
						UseShellExecute = true,
						WindowStyle = ProcessWindowStyle.Hidden
					}
				};
				process.Start();
				process.WaitForExit();
			}

			private bool _isRunning = false;

			public void Start() {
				//Thread.Sleep(5000); //Uncomment to give enough time to hook for attach process debugging
				if (_isRunning) {
					Console.WriteLine("MCP server is already running.");
					return;
				}

				try {
					_listener.Start();
					_listener.BeginGetContext(OnRequest, null);
					_isRunning = true;
					//Console.WriteLine("MCP server started. CurrentlyDebugging: " + Bridge.DbgIsDebugging() + " IsRunning: " + Bridge.DbgIsRunning());
				}
				catch (Exception ex) {
					Console.WriteLine("Failed to start MCP server: " + ex.Message);
				}
			}
















			public void Stop() {
				if (!_isRunning) {
					Console.WriteLine("MCP server is already stopped.");
					_isRunning = false;
					return;
				}

				try {
					_listener.Stop();
					_isRunning = false;
					Console.WriteLine("MCP server stopped.");
				}
				catch (Exception ex) {
					Console.WriteLine("Failed to stop MCP server: " + ex.Message);
				}
			}


			public static void PrettyPrintJson(string json)
			{
				try {
					using JsonDocument doc = JsonDocument.Parse(json);
					string prettyJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions {
						WriteIndented = true
					});

					var compact = string.Join(Environment.NewLine,
					prettyJson.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd()));

					Console.WriteLine(compact.Replace("{", "").Replace("}", "").Replace("\r", ""));
				}
				catch (JsonException ex) {
					Console.WriteLine("Invalid JSON: " + ex.Message);
				}
			}

			static bool pDebug = false;
			private static readonly Dictionary<string, StreamWriter> _sseSessions = new Dictionary<string, StreamWriter>();

			private async void OnRequest(IAsyncResult ar) // Make async void for simplicity here, consider Task for robustness
			{
				HttpListenerContext ctx = null; // Initialize to null
				try {
					ctx = _listener.EndGetContext(ar);
				}
				catch (ObjectDisposedException) {
					Console.WriteLine("Listener was stopped.");
					return; // Listener was stopped, exit gracefully
				}
				catch (Exception ex) {
					Console.WriteLine($"Error getting listener context: {ex.Message}");
					// Potentially try to restart listening or log fatal error
					return; // Cannot process request
				}


				// Keep listening for the next request
				try {
					if (_listener.IsListening) {
						_listener.BeginGetContext(OnRequest, null); // loop
					}
				}
				catch (ObjectDisposedException) {
					// Listener was stopped between EndGetContext and BeginGetContext, ignore.
				}
				catch (Exception ex) {
					Console.WriteLine($"Error restarting listener loop: {ex.Message}");
					// Consider implications if listening cannot be restarted
				}


				if (pDebug) {
					Console.WriteLine("=== Incoming Request ===");
					Console.WriteLine($"Method: {ctx.Request.HttpMethod}");
					Console.WriteLine($"URL: {ctx.Request.Url}");
					Console.WriteLine($"Headers:");
					foreach (string key in ctx.Request.Headers) {
						Console.WriteLine($"  {key}: {ctx.Request.Headers[key]}");
					}
					Console.WriteLine("=========================");
				}
				string requestBody = null; // Variable to store the body
										   // ctx.Response.Headers["Server"] = "MyCustomServer/1.0"; // Example custom header

				if (ctx.Request.HttpMethod == "POST") {
					// --- Existing POST handler logic ... ---
					var path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

					if (path.StartsWith("/message")) {
						var sessionId = ctx.Request.QueryString["sessionId"]; // Renamed from 'query' for clarity
						bool sessionIsValid = false;
						lock (_sseSessions) {
							sessionIsValid = !string.IsNullOrWhiteSpace(sessionId) && _sseSessions.ContainsKey(sessionId);
						}

						if (!sessionIsValid) {
							Console.WriteLine($"Bad request for /message: Invalid or missing sessionId '{sessionId}'");
							ctx.Response.StatusCode = 400; // Bad Request
														   // Optionally write a response body
							byte[] badReqBuffer = Encoding.UTF8.GetBytes("Invalid or missing sessionId.");
							ctx.Response.ContentType = "text/plain; charset=utf-8";
							ctx.Response.ContentLength64 = badReqBuffer.Length;
							try {
								ctx.Response.OutputStream.Write(badReqBuffer, 0, badReqBuffer.Length);
							}
							catch (Exception writeEx) { Console.WriteLine($"Error writing 400 response: {writeEx.Message}"); }
							finally { ctx.Response.OutputStream.Close(); }
							return;
						}

						// --- Read Body ---
						string jsonBody = "";
						if (ctx.Request.HasEntityBody) {
							using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding)) // Use correct encoding
							{
								jsonBody = await reader.ReadToEndAsync(); // Use async read
								if (pDebug) { Debug.WriteLine("jsonBody:" + jsonBody); }
							}
						}
						else {
							if (pDebug) { Console.WriteLine("No body."); }
							// Handle case with no body if necessary, maybe depends on method called?
							// For now, assume methods require a body. If not, handle json being null later.
						}


						// --- Respond 202 Accepted Immediately ---
						// (Do this *before* processing the body to conform to async MCP pattern)
						try {
							ctx.Response.StatusCode = 202; // Accepted
							ctx.Response.ContentType = "text/plain; charset=utf-8";
							byte[] buffer = Encoding.UTF8.GetBytes("Accepted");
							ctx.Response.ContentLength64 = buffer.Length; // Set content length
							await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
							await ctx.Response.OutputStream.FlushAsync();
							// IMPORTANT: Do NOT close the response stream here. It's needed for the SSE connection.
							// The HttpListener keeps the connection open implicitly after headers/initial body are sent.
						}
						catch (Exception acceptEx) {
							Console.WriteLine($"Error sending 202 Accepted: {acceptEx.Message}");
							// Cannot continue processing if 202 failed. Maybe cleanup SSE?
							CleanupSseSession(sessionId);
							return; // Stop processing this request
						}


						// --- Process Request Body (JSON RPC) ---
						Dictionary<string, object> json = null;
						object rpcId = null; // Store ID for responses/errors
						string method = null;

						try {
							if (string.IsNullOrWhiteSpace(jsonBody)) {
								throw new JsonException("Request body is empty or whitespace.");
							}
							// Use JavaScriptSerializer as per existing code
							json = _jsonSerializer.Deserialize<Dictionary<string, object>>(jsonBody);

							if (json == null) {
								throw new JsonException("Failed to deserialize JSON body.");
							}

							// Extract essential JSON RPC fields
							json.TryGetValue("id", out rpcId); // Can be string or number, keep as object
							if (!json.TryGetValue("method", out object methodObj) || !(methodObj is string)) {
								// Send error via SSE if ID is known, otherwise just log
								var errorMsg = "Invalid JSON RPC: Missing or invalid 'method'.";
								Console.WriteLine($"Error processing request for session {sessionId}: {errorMsg}");
								if (rpcId != null) SendSseError(sessionId, rpcId, -32600, errorMsg); // Invalid Request
								return; // Stop processing
							}
							method = (string)methodObj;

							if (pDebug) { Console.WriteLine($"RPC Call | Session: {sessionId}, ID: {rpcId}, Method: {method}"); }

							// --- Method Dispatching ---
							if (method == "rpc.discover") // Legacy? Or just basic tool list? Treat like tools/list.
							{
								HandleToolsList(sessionId, rpcId); // Use helper
							}
							else if (method == "initialize") {
								HandleInitialize(sessionId, rpcId, json); // Use helper
							}
							else if (method == "notifications/initialized") {
								if (pDebug) { Console.WriteLine($"Notification 'initialized' received for session {sessionId}."); }
								// No response needed for notifications
							}
							else if (method == "tools/list") {
								HandleToolsList(sessionId, rpcId); // Use helper
							}
							else if (method == "tools/call") {
								HandleToolCall(sessionId, rpcId, json); // Use helper
							}
							// --- *** NEW HANDLERS START HERE *** ---
							else if (method == "prompts/list") {
								HandlePromptsList(sessionId, rpcId); // Use helper
							}
							else if (method == "prompts/get") {
								HandlePromptsGet(sessionId, rpcId, json); // Use helper
							}
							else if (method == "resources/list") {
								HandleResourcesList(sessionId, rpcId); // Use helper
							}
							// --- *** NEW HANDLERS END HERE *** ---
							else if (_commands.TryGetValue(method, out var methodInfo)) // Check for legacy direct command call
							{
								// Handle legacy direct calls if needed, otherwise treat as unknown
								Console.WriteLine($"Warning: Received legacy-style direct command call '{method}' for session {sessionId}. Consider using 'tools/call'.");
								// Send Method Not Found or handle if intended
								SendSseError(sessionId, rpcId, -32601, $"Direct command calls are deprecated. Use 'tools/call' for method '{method}'.");
							}
							else {
								Console.WriteLine($"Unknown method '{method}' received for session {sessionId}");
								SendSseError(sessionId, rpcId, -32601, $"Method not found: {method}"); // Method Not Found
							}
						}
						catch (JsonException jsonEx) {
							Console.WriteLine($"JSON Error processing request for session {sessionId}: {jsonEx.Message}");
							// Try to send Parse Error (-32700). ID might be null if parsing failed early.
							SendSseError(sessionId, rpcId, -32700, $"Parse error: Invalid JSON received. ({jsonEx.Message})");
						}
						catch (Exception ex) // Catch other errors during dispatch/processing
						{
							Console.WriteLine($"Error processing method '{method ?? "unknown"}' for session {sessionId}: {ex}");
							// Send Internal Server Error (-32000 or -32603)
							SendSseError(sessionId, rpcId, -32603, $"Internal error processing method '{method ?? "unknown"}': {ex.Message}");
						}
					}
					else // Path doesn't start with /message
					{
						Console.WriteLine($"POST request to unknown path: {path}");
						ctx.Response.StatusCode = 404; // Not Found
						ctx.Response.OutputStream.Close();
					}
				} // End POST Handling
				else if (ctx.Request.HttpMethod == "GET") {
					// --- Existing GET handler logic ---
					var path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();

					// --- Handle SSE Connection ---
					if (path.EndsWith("/sse/") || path.EndsWith("/sse")) {
						// ** IMPORTANT: This GET handler MUST keep the connection open **
						ctx.Response.ContentType = "text/event-stream; charset=utf-8"; // Specify UTF-8
						ctx.Response.StatusCode = 200;
						ctx.Response.SendChunked = true; // Important for streaming
						ctx.Response.KeepAlive = true; // Ensure connection stays open
						ctx.Response.Headers.Add("Cache-Control", "no-cache"); // Prevent caching of the stream
						ctx.Response.Headers.Add("X-Accel-Buffering", "no"); // Useful for Nginx environments

						string sessionId = "";
						try {
							// Generate a URL-safe session ID
							using (var rng = RandomNumberGenerator.Create()) {
								byte[] randomBytes = new byte[16]; // 128 bits
								rng.GetBytes(randomBytes);
								// Base64Url encoding removes padding and replaces URL-unsafe chars
								sessionId = Convert.ToBase64String(randomBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
							}

							// Use UTF8Encoding(false) to avoid BOM. Leave stream open. AutoFlush is good.
							var writer = new StreamWriter(ctx.Response.OutputStream, new UTF8Encoding(false), 1024, leaveOpen: true) {
								AutoFlush = true // Ensure data is sent immediately
							};

							bool added = false;
							lock (_sseSessions) {
								// Check for collision, though highly unlikely
								if (!_sseSessions.ContainsKey(sessionId)) {
									_sseSessions[sessionId] = writer;
									added = true;
								}
								else {
									// Handle extremely rare collision if necessary
									Console.WriteLine($"WARNING: Session ID collision detected for {sessionId}");
									// Could regenerate ID or return an error
								}
							}

							if (added) {
								Console.WriteLine($"SSE session started: {sessionId}");
								// Write required handshake format
								// Ensure correct path based on how server is hosted/proxied if needed
								string messagePath = $"/message?sessionId={sessionId}";
								await writer.WriteAsync($"event: endpoint\n");
								await writer.WriteAsync($"data: {messagePath}\n\n");
								// Flush is handled by AutoFlush=true, but an explicit one after handshake is safe.
								// await writer.FlushAsync();

								// Keep the connection open. The HttpListener context will manage this.
								// We don't close the writer or the response stream here.
								// Need a way to detect client disconnect. HttpListener doesn't have a direct token like ASP.NET Core.
								// One common way is to periodically try writing a comment and catch exceptions.
								// Or rely on the SendData method's exception handling to trigger cleanup.
								// For this example, we'll rely on SendData failures for cleanup.
								// A more robust implementation might have a separate task monitoring connections.

							}
							else {
								// Failed to add session (e.g., collision)
								ctx.Response.StatusCode = 500; // Internal Server Error
															   // Close response here as session wasn't established
								writer?.Dispose(); // Dispose writer if created
								ctx.Response.OutputStream.Close();
							}
						}
						catch (Exception ex) {
							Console.WriteLine($"Error establishing SSE session or sending handshake: {ex.Message}");
							// Attempt to set error status code ONLY if no headers have been sent
							// (which we can't reliably check, so we just try)
							try {
								// Check if response has started - HttpListenerException might occur if already started
								if (ctx.Response.StatusCode == 200) // Only try if we haven't already set an error elsewhere
								{
									ctx.Response.StatusCode = 500;
								}
							}
							catch (Exception statusEx) {
								Console.WriteLine($"Could not set error status code for SSE setup failure: {statusEx.Message}");
							}

							// Ensure response is closed if an error occurs during setup
							try { ctx.Response.OutputStream.Close(); } catch { } // Close underlying stream
																				 // Cleanup session if partially added
							if (!string.IsNullOrEmpty(sessionId)) { CleanupSseSession(sessionId); }
						}
					}
					// --- Handle Legacy Discover (Optional, tools/list is preferred) ---
					else if (path.EndsWith("/discover") || path.EndsWith("/mcp/")) // Assuming /mcp/ is legacy GET
					{
						// This is a synchronous response, not SSE
						// Consider if this endpoint is actually needed if using MCP over SSE
						Console.WriteLine("Handling legacy GET /discover or /mcp/");
						var toolList = new List<object>();
						lock (_commands) // Lock if _commands could be modified elsewhere
						{
							foreach (var cmd in _commands) {
								var methodInfo = cmd.Value;
								var attribute = methodInfo.GetCustomAttribute<CommandAttribute>();
								if (attribute != null && (!attribute.DebugOnly /* || Debugger.IsAttached *//* || Add Bridge checks if needed */)) {
									toolList.Add(new {
										name = cmd.Key,
										// Legacy discover often had simpler parameter info
										parameters = methodInfo.GetParameters().Select(p => $"{p.ParameterType.Name}").ToArray()
									});
								}
							}
						}


						var legacyResponse = new // Structure might differ for legacy
						{
							jsonrpc = "2.0",
							id = (string)null, // No ID for spontaneous discover usually
							result = toolList // Or maybe a different structure
						};
						var json = _jsonSerializer.Serialize(legacyResponse);
						var buffer = Encoding.UTF8.GetBytes(json);
						ctx.Response.ContentType = "application/json; charset=utf-8";
						ctx.Response.ContentLength64 = buffer.Length;
						try {
							await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
						}
						catch (Exception writeEx) { Console.WriteLine($"Error writing discover response: {writeEx.Message}"); }
						finally { ctx.Response.OutputStream.Close(); } // Close sync response
					}
					else // Unknown GET path
					{
						Console.WriteLine($"GET request to unknown path: {path}");
						ctx.Response.StatusCode = 404; // Not Found
						ctx.Response.OutputStream.Close();
					}
				} // End GET Handling
				else // Other HTTP methods (PUT, DELETE, etc.)
				{
					Console.WriteLine($"Unsupported HTTP method: {ctx.Request.HttpMethod}");
					ctx.Response.StatusCode = 405; // Method Not Allowed
					ctx.Response.AddHeader("Allow", "GET, POST");
					ctx.Response.OutputStream.Close();
				}
			} // End OnRequest Method


			// --- Additions: Specific MCP Method Handlers ---

			private void HandleInitialize(string sessionId, object id, Dictionary<string, object> json) {
				// Process clientInfo or capabilities from json["params"] if needed
				// Example: var clientInfo = (json["params"] as Dictionary<string, object>)?["clientInfo"];

				var result = new {
					protocolVersion = "2024-11-05", // The version this server implements
					capabilities = new {
						tools = new { }, // Indicate tool support
						prompts = new { }, // Indicate prompt support
						resources = new { } // Indicate resource support
					},
					serverInfo = new { name = "AgentSmithers-dnSpyExMcpServer", version = "1.0.0" }, // Your server details
					instructions = "Welcome to the AgentSmithers dnSpyEx MCP Server!" // Optional instructions
				};
				SendSseResult(sessionId, id, result);
			}

			private void HandleToolsList(string sessionId, object id) {
				var toolsList = new List<object>();
				bool isDebuggerAttached = true;//Bridge.DbgIsDebugging();
				bool isDbgRunning = true; //Bridge.DbgIsRunning();
				// Lock if _commands could be modified concurrently
				lock (_commands) {
					foreach (var command in _commands) {
						string commandName = command.Key;
						MethodInfo methodInfo = command.Value;
						var attribute = methodInfo.GetCustomAttribute<CommandAttribute>();
						if (attribute != null && (!attribute.DebugOnly || (isDebuggerAttached && IsActivelyDebugging || true) )) {
							var parameters = methodInfo.GetParameters();
							var properties = new Dictionary<string, object>();
							var required = new List<string>();

							foreach (var param in parameters) {
								string paramName = param.Name;
								string paramType = GetJsonSchemaType(param.ParameterType);
								// --- CORRECTED: Use generic description as fallback ---
								string paramDescription = $"Parameter '{paramName}' for {commandName}";
								// --- You could add specific descriptions here based on param.Name if desired ---
								// Example: if (paramName == "address") { paramDescription = "Memory address..."; }


								properties[paramName] = new { type = paramType, description = paramDescription };

								if (!param.IsOptional) {
									required.Add(paramName);
								}
							}

							// MCP Tool Definition structure
							toolsList.Add(new {
								name = commandName,
								description = string.IsNullOrEmpty(attribute.MCPCmdDescription) ? $"Executes the {commandName} command." : attribute.MCPCmdDescription,
								inputSchema = new {
									title = commandName,
									description = string.IsNullOrEmpty(attribute.MCPCmdDescription) ? $"Input schema for {commandName}." : attribute.MCPCmdDescription,
									type = "object",
									properties = properties,
									required = required.ToArray()
								}
							});
						}
					}
				}

				// Add built-in Echo if desired (though the main handler already has it)
				/*
				toolsList.Add(new {
					name = "Echo",
					description = "Echoes the input back.",
					inputSchema = new {
						title = "Echo", description = "Echo Input", type = "object",
						properties = new { message = new { type = "string", description = "Message to echo" } },
						required = new[] { "message" }
					}
				});
				*/

				SendSseResult(sessionId, id, new { tools = toolsList.ToArray() });
			}

			private void HandleToolCall(string sessionId, object id, Dictionary<string, object> json) {
				string toolName = null;
				Dictionary<string, object> arguments = null;
				string resultText = "An error occurred processing the tool call."; // Default error text
				bool isError = true; // Default to error

				try {
					// --- Parse tool call parameters ---
					if (!json.TryGetValue("params", out object paramsObj) || !(paramsObj is Dictionary<string, object> paramsDict)) {
						throw new ArgumentException("Invalid or missing 'params' object for tools/call");
					}

					if (!paramsDict.TryGetValue("name", out object nameObj) || !(nameObj is string) || string.IsNullOrWhiteSpace((string)nameObj)) {
						throw new ArgumentException("Invalid or missing 'name' in tools/call params");
					}
					toolName = (string)nameObj;

					// Arguments are optional in the spec, but usually present if needed
					if (paramsDict.TryGetValue("arguments", out object argsObj) && argsObj is Dictionary<string, object> argsDict) {
						arguments = argsDict;
					}
					else {
						arguments = new Dictionary<string, object>(); // Ensure arguments is not null
					}

					if (pDebug) { Console.WriteLine($"Tool Call: {toolName} with args: {(arguments.Count > 0 ? _jsonSerializer.Serialize(arguments) : "None")}"); }


					// --- Execute Tool ---
					MethodInfo methodInfo;
					bool commandFound;
					lock (_commands) // Lock if _commands can be modified
					{
						commandFound = _commands.TryGetValue(toolName, out methodInfo);
					}

					if (commandFound) {
						var attribute = methodInfo.GetCustomAttribute<CommandAttribute>();
						// Check permissions/filters again if necessary
						if (attribute == null  || (attribute.DebugOnly && !IsActivelyDebugging /* && !Debugger.IsAttached  && !Bridge.DbgIsDebugging() */ )) {
							throw new InvalidOperationException($"Command '{toolName}' is not available in this context, you must begin debugging an application first!");
						}

						// Prepare arguments for invocation
						var parameters = methodInfo.GetParameters();
						var invokeArgs = new object[parameters.Length];
						for (int i = 0; i < parameters.Length; i++) {
							var param = parameters[i];
							object argValue = null;
							bool argProvided = arguments.TryGetValue(param.Name, out argValue);

							if (argProvided && argValue != null) {
								try {
									// Use helper for type conversion
									invokeArgs[i] = ConvertArgumentType(argValue, param.ParameterType, param.Name);
								}
								catch (Exception convEx) {
									throw new ArgumentException($"Cannot convert argument '{param.Name}' for tool '{toolName}'. Error: {convEx.Message}", convEx);
								}
							}
							else if (param.IsOptional) {
								invokeArgs[i] = param.DefaultValue;
							}
							else // Required parameter is missing or null
							{
								throw new ArgumentException($"Missing required argument: '{param.Name}' for tool '{toolName}'");
							}
						}

						// Invoke the static method
						var result = methodInfo.Invoke(null, invokeArgs);
						resultText = result?.ToString() ?? $"{toolName} executed successfully.";
						isError = false; // Success
					}
					else if (toolName.Equals("Echo", StringComparison.OrdinalIgnoreCase)) {
						// Handle built-in Echo if needed (or rely on CommandImplementations)
						object messageArg = null;
						if (arguments.TryGetValue("message", out messageArg) && messageArg != null) {
							resultText = $"Echo response: {messageArg}";
							isError = false;
						}
						else {
							resultText = "Echo tool called without 'message' argument.";
							isError = true;
						}
					}
					else {
						resultText = $"Tool '{toolName}' not found.";
						isError = true; // Keep isError true
					}
				}
				catch (TargetInvocationException tie) // Exception thrown by the invoked method
				{
					resultText = $"Error executing tool '{toolName}': {(tie.InnerException?.Message ?? tie.Message)}";
					isError = true;
					Console.WriteLine($"Execution Error in {toolName}: {(tie.InnerException ?? tie)}");
				}
				catch (Exception ex) // Error during parsing, binding, or invocation setup
				{
					resultText = $"Error processing tool call for '{toolName ?? "unknown"}': {ex.Message}";
					isError = true;
					Console.WriteLine($"Error during tool call {toolName ?? "unknown"}: {ex}");
				}

				// --- Send Result ---
				// MCP tools/call result format
				var toolContent = new[] { new { type = "text", text = resultText } };
				var callResult = new { content = toolContent, isError = isError };
				SendSseResult(sessionId, id, callResult);
			}

			// --- *** PROMPTS/LIST Handler *** ---
			private void HandlePromptsList(string sessionId, object id) {
				if (pDebug) { Console.WriteLine($"Handling prompts/list for session {sessionId}"); }
				var promptsList = new List<PromptInfo>(); // Use the defined class
				try {
					// No lock needed for reading readonly _prompts generally, but add if modification is possible
					lock (_prompts) {
						promptsList.AddRange(_prompts);
					}

					// Send the result { prompts: [...] }
					var result = new PromptsListResult { prompts = promptsList };
					SendSseResult(sessionId, id, result);
				}
				catch (Exception ex) {
					Console.WriteLine($"Error handling prompts/list for session {sessionId}: {ex}");
					SendSseError(sessionId, id, -32603, $"Internal error handling prompts/list: {ex.Message}");
				}
			}

			// --- *** PROMPTS/GET Handler *** ---
			private void HandlePromptsGet(string sessionId, object id, Dictionary<string, object> json) {
				string promptName = null;
				Dictionary<string, object> arguments = null;
				PromptInfo promptInfo = null;

				try {
					// --- Parse Input ---
					if (!json.TryGetValue("params", out object paramsObj) || !(paramsObj is Dictionary<string, object> paramsDict)) {
						throw new ArgumentException("Invalid or missing 'params' object for prompts/get");
					}

					if (!paramsDict.TryGetValue("name", out object nameObj) || !(nameObj is string) || string.IsNullOrWhiteSpace((string)nameObj)) {
						throw new ArgumentException("Invalid or missing 'name' in prompts/get params");
					}
					promptName = (string)nameObj;

					if (paramsDict.TryGetValue("arguments", out object argsObj) && argsObj is Dictionary<string, object> argsDict) {
						arguments = argsDict;
					}
					else {
						arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); // Use case-insensitive for lookups later
					}

					if (pDebug) { Console.WriteLine($"Handling prompts/get: {promptName} with args: {(arguments.Count > 0 ? _jsonSerializer.Serialize(arguments) : "None")}"); }

					// --- Find Prompt ---
					bool promptFound;
					lock (_prompts) // Lock if _prompts could be modified
					{
						promptInfo = _prompts.FirstOrDefault(p => p.name.Equals(promptName, StringComparison.OrdinalIgnoreCase));
					}

					if (promptInfo == null) {
						SendSseError(sessionId, id, -32601, $"Prompt not found: {promptName}"); // Method/Prompt not found
						return;
					}

					// --- Validate Required Arguments ---
					if (promptInfo.arguments != null) {
						foreach (var requiredArg in promptInfo.arguments.Where(a => a.required == true)) {
							object argValue = null;
							if (arguments == null || !arguments.TryGetValue(requiredArg.name, out argValue) || argValue == null) {
								throw new ArgumentException($"Missing required argument '{requiredArg.name}' for prompt '{promptName}'.");
							}
						}
					}

					// --- Perform Argument Substitution ---
					var generatedMessages = new List<object>(); // List for final JSON messages
					if (promptInfo.messageTemplates != null) {
						foreach (var template in promptInfo.messageTemplates) {
							string originalText = template.content?.text;
							string substitutedText = originalText ?? "";

							if (!string.IsNullOrEmpty(substitutedText) && promptInfo.arguments != null) {
								foreach (var argDef in promptInfo.arguments) {
									string placeholder = $"{{{argDef.name}}}";
									if (substitutedText.Contains(placeholder)) {
										object argValueObj = null;
										string actualArgValueString = ""; // Default empty

										if (arguments != null && arguments.TryGetValue(argDef.name, out argValueObj) && argValueObj != null) {
											actualArgValueString = Convert.ToString(argValueObj);
										}
										else if (argDef.required != true) // Optional arg is missing
										{
											actualArgValueString = ""; // Replace placeholder with empty
										}
										// Else: Required arg missing (should have been caught, but defensive) - leave placeholder?

										substitutedText = substitutedText.Replace(placeholder, actualArgValueString);
									}
								}

								// Handle special optional placeholder example: {maxLengthPlaceholder}
								string maxLengthPlaceholder = "{maxLengthPlaceholder}";
								if (substitutedText.Contains(maxLengthPlaceholder)) {
									object maxLengthObj = null;
									string maxLengthText = ""; // Disappears if arg missing
									if (arguments != null && arguments.TryGetValue("maxLength", out maxLengthObj) && maxLengthObj != null) {
										maxLengthText = $" (max length: {maxLengthObj})"; // Appears if arg present
									}
									substitutedText = substitutedText.Replace(maxLengthPlaceholder, maxLengthText);
								}
							}

							// Add the fully substituted message using the FinalPromptMessage structure for clarity
							generatedMessages.Add(new FinalPromptMessage {
								role = template.role,
								content = new FinalPromptContent { type = template.content?.type ?? "text", text = substitutedText }
							});
						}
					}

					// --- Send Result ---
					var result = new PromptGetResult {
						description = promptInfo.description,
						messages = generatedMessages
					};
					SendSseResult(sessionId, id, result);

				}
				catch (ArgumentException argEx) // Catch specific argument errors (parsing, validation)
				{
					Console.WriteLine($"Argument Error handling prompts/get for '{promptName ?? "unknown"}' (Session: {sessionId}): {argEx.Message}");
					SendSseError(sessionId, id, -32602, $"Invalid parameters: {argEx.Message}"); // Invalid Params
				}
				catch (Exception ex) // Catch other processing errors
				{
					Console.WriteLine($"Error handling prompts/get for '{promptName ?? "unknown"}' (Session: {sessionId}): {ex}");
					SendSseError(sessionId, id, -32603, $"Internal error processing prompt '{promptName ?? "unknown"}': {ex.Message}"); // Internal Error
				}
			}

			// --- *** RESOURCES/LIST Handler *** ---
			private void HandleResourcesList(string sessionId, object id) {
				if (pDebug) { Console.WriteLine($"Handling resources/list for session {sessionId}"); }
				var resourcesList = new List<object>(); // Combined list

				try {
					// Add static resources
					lock (_resources) // Lock if modification is possible
					{
						foreach (var kvp in _resources) {
							// Add the ResourceInfo object directly (assuming property names match JSON)
							resourcesList.Add(kvp.Value);
						}
					}

					// Add resource templates
					lock (_resourceTemplates) // Lock if modification is possible
					{
						foreach (var kvp in _resourceTemplates) {
							// Add the ResourceTemplateInfo object directly
							resourcesList.Add(kvp.Value);
						}
					}

					// Send the result { resources: [...] }
					var result = new ResourceListResult { resources = resourcesList };
					SendSseResult(sessionId, id, result);
				}
				catch (Exception ex) {
					Console.WriteLine($"Error handling resources/list for session {sessionId}: {ex}");
					SendSseError(sessionId, id, -32603, $"Internal error handling resources/list: {ex.Message}");
				}
			}

			// --- Additions: Helper for Argument Conversion ---
			private object ConvertArgumentType(object argValue, Type requiredType, string paramName) {
				if (argValue == null) {
					if (requiredType.IsClass || Nullable.GetUnderlyingType(requiredType) != null) return null;
					throw new ArgumentNullException(paramName, $"Null provided for non-nullable parameter '{paramName}' of type {requiredType.Name}");
				}

				// If type already matches (common for strings, bools when deserialized)
				if (requiredType.IsInstanceOfType(argValue)) return argValue;

				// Handle numeric types explicitly, as JavaScriptSerializer might deserialize numbers as int, double, or decimal
				if (requiredType == typeof(int)) return Convert.ToInt32(argValue);
				if (requiredType == typeof(long)) return Convert.ToInt64(argValue);
				if (requiredType == typeof(short)) return Convert.ToInt16(argValue);
				if (requiredType == typeof(byte)) return Convert.ToByte(argValue);
				if (requiredType == typeof(uint)) return Convert.ToUInt32(argValue);
				if (requiredType == typeof(ulong)) return Convert.ToUInt64(argValue);
				if (requiredType == typeof(ushort)) return Convert.ToUInt16(argValue);
				if (requiredType == typeof(sbyte)) return Convert.ToSByte(argValue);
				if (requiredType == typeof(float)) return Convert.ToSingle(argValue);
				if (requiredType == typeof(double)) return Convert.ToDouble(argValue);
				if (requiredType == typeof(decimal)) return Convert.ToDecimal(argValue);
				if (requiredType == typeof(bool)) return Convert.ToBoolean(argValue);
				if (requiredType == typeof(Guid)) return Guid.Parse(argValue.ToString());

				if (requiredType.IsEnum) return System.Enum.Parse(requiredType, argValue.ToString(), ignoreCase: true);

				// Handle simple arrays (e.g., string[] from List<object>)
				if (requiredType.IsArray && argValue is System.Collections.ArrayList list) {
					var elementType = requiredType.GetElementType();
					var typedArray = Array.CreateInstance(elementType, list.Count);
					for (int j = 0; j < list.Count; j++) {
						try {
							typedArray.SetValue(Convert.ChangeType(list[j], elementType), j);
						}
						catch (Exception ex) {
							throw new InvalidCastException($"Cannot convert array element '{list[j]}' to type '{elementType.Name}' for parameter '{paramName}'.", ex);
						}
					}
					return typedArray;
				}
				// Handle simple arrays (e.g., string[] from object[]) - JSON deserializer might give object[]
				if (requiredType.IsArray && argValue is object[] objArray) {
					var elementType = requiredType.GetElementType();
					var typedArray = Array.CreateInstance(elementType, objArray.Length);
					for (int j = 0; j < objArray.Length; j++) {
						try {
							typedArray.SetValue(Convert.ChangeType(objArray[j], elementType), j);
						}
						catch (Exception ex) {
							throw new InvalidCastException($"Cannot convert array element '{objArray[j]}' to type '{elementType.Name}' for parameter '{paramName}'.", ex);
						}
					}
					return typedArray;
				}


				// Fallback for other types
				try {
					return Convert.ChangeType(argValue, requiredType, System.Globalization.CultureInfo.InvariantCulture);
				}
				catch (Exception ex) {
					throw new InvalidCastException($"Cannot convert value '{argValue}' (type: {argValue.GetType().Name}) to required type '{requiredType.Name}' for parameter '{paramName}'.", ex);
				}
			}
			// --- End Additions ---





























			// Helper method to convert C# types to JSON schema types
			private string GetJsonSchemaType(Type type) {
				if (type == typeof(string))
					return "string";
				else if (type == typeof(int) || type == typeof(long) || type == typeof(short) ||
						 type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort))
					return "integer";
				else if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
					return "number";
				else if (type == typeof(bool))
					return "boolean";
				else if (type.IsArray)
					return "array";
				else
					return "object";
			}









			// --- Additions: SSE Helper Methods ---
			private void SendSseResult(string sessionId, object id, object result) {
				var response = new JsonRpcResponse<object> { id = id, result = result };
				string jsonData = _jsonSerializer.Serialize(response);
				SendData(sessionId, jsonData);
			}

			private void SendSseError(string sessionId, object id, int code, string message, object data = null) {
				var errorPayload = new JsonRpcError { code = code, message = message, data = data };
				var response = new JsonRpcErrorResponse { id = id, error = errorPayload };
				string jsonData = _jsonSerializer.Serialize(response);
				SendData(sessionId, jsonData);
			}

			private void SendData(string sessionId, string jsonData) {
				// IMPORTANT: Error handling around _sseSessions access and writing is crucial
				// The existing code uses lock(_sseSessions) - maintain that pattern.
				try {
					StreamWriter writer;
					bool sessionExists;
					lock (_sseSessions) {
						sessionExists = _sseSessions.TryGetValue(sessionId, out writer);
					}

					if (sessionExists && writer != null) {
						// Ensure thread safety when writing if writer is accessed elsewhere,
						// although lock above might suffice if access is always locked.
						// Consider locking the writer object itself if necessary,
						// but be wary of deadlocks.
						lock (writer) // Lock the specific writer for thread-safe write/flush
						{
							// Write standard SSE format: data: json\n\n
							writer.Write($"data: {jsonData}\n\n");
							writer.Flush(); // Ensure data is sent immediately
							if (pDebug) { Console.WriteLine($"SSE >>> Session {sessionId}: {jsonData}"); }
						}
					}
					else {
						Console.WriteLine($"Error: SSE Session {sessionId} not found or writer is null when trying to send data.");
						// Optionally remove the session if writer is null?
					}
				}
				catch (ObjectDisposedException) {
					Console.WriteLine($"SSE Session {sessionId} writer was disposed. Cleaning up.");
					CleanupSseSession(sessionId);
				}
				catch (IOException ioEx) // Catches broken pipe etc.
				{
					Console.WriteLine($"SSE Write Error for session {sessionId}: {ioEx.Message}. Cleaning up.");
					CleanupSseSession(sessionId);
				}
				catch (Exception ex) {
					Console.WriteLine($"Unexpected error sending SSE data for session {sessionId}: {ex}");
					// Consider cleanup here too
					CleanupSseSession(sessionId);
				}
			}

			private void CleanupSseSession(string sessionId) {
				lock (_sseSessions) {
					if (_sseSessions.TryGetValue(sessionId, out StreamWriter writer)) {
						try {
							writer?.Dispose();
						}
						catch (Exception ex) {
							Console.WriteLine($"Error disposing writer for session {sessionId}: {ex.Message}");
						}
						finally {
							_sseSessions.Remove(sessionId);
							Console.WriteLine($"Removed SSE session {sessionId}.");
						}
					}
				}
			}
			// --- End Additions: SSE Helper Methods ---







			// --- Additions for Prompts and Resources ---
			private static readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer(); // Re-use serializer

			// Using a List to store prompts. The 'name' property within PromptInfo is used for identification.
			private readonly List<PromptInfo> _prompts = new List<PromptInfo>
			{
			new PromptInfo {
				name = "dnSpyEx Prompt", // Unique name stored inside the object
                description = "Prompt used as a default to ask the AI to use the dnSpyEx functionality",
				arguments = new List<PromptArgument> { /* Empty */ },
				messageTemplates = new List<PromptMessageTemplate> {
					new PromptMessageTemplate {
						role = "user",
						content = new PromptContentTemplate { type = "text", text = "You are an AI assistant with access to an MCP (Model Context Protocol) server. Your goal is to complete tasks by calling the available commands on this server which is connected to dnSpyEx designed for decompiling .NET applications." }
					}
				}
			}
            /*
            ,
            new PromptInfo {
                name = "Query-a-client-name", // Unique name stored inside the object
                description = "You are a helpful AI that will query for a client's name using the MCP calls",
                arguments = new List<PromptArgument> {
                    new PromptArgument { name = "text", description = "Client name search term", required = true },
                    new PromptArgument { name = "maxLength", description = "Maximum client's name length", required = false }
                },
                messageTemplates = new List<PromptMessageTemplate> {
                    new PromptMessageTemplate {
                        role = "user",
                        content = new PromptContentTemplate { type = "text", text = "Query the client database for names containing: \"{text}\"{maxLengthPlaceholder}." } // Using placeholder for optional part
                    }
                }
            },
            new PromptInfo {
                name = "Debug-Error-Workflow", // Unique name stored inside the object
                description = "Guides the user through debugging a reported error.",
                arguments = new List<PromptArgument> {
                    new PromptArgument { name = "errorMessage", description = "The initial error message reported by the user", required = true }
                },
                messageTemplates = new List<PromptMessageTemplate> {
                    new PromptMessageTemplate { // Message 1
                        role = "user",
                        content = new PromptContentTemplate { type = "text", text = "I'm encountering an error: \"{errorMessage}\"" }
                    },
                    new PromptMessageTemplate { // Message 2
                        role = "assistant",
                        content = new PromptContentTemplate { type = "text", text = "Okay, I see the error message. To help diagnose this, could you tell me what steps you took leading up to this error, and what you've already tried to resolve it?" }
                    },
                    new PromptMessageTemplate { // Message 3 (Simulated User)
                        role = "user",
                        content = new PromptContentTemplate { type = "text", text = "I was trying to process the monthly report. I've already tried restarting the application server, but the error persists." }
                    },
                    new PromptMessageTemplate { // Message 4
                        role = "assistant",
                        content = new PromptContentTemplate { type = "text", text = "Got it. Restarting didn't help. Could you please check the latest application logs (e.g., `/var/log/app/error.log`) for any specific entries around the time the error occurred? Any stack traces or related warnings would be helpful." }
                    }
                }
            }
            */
        };

			private readonly Dictionary<string, ResourceInfo> _resources = new Dictionary<string, ResourceInfo>(StringComparer.OrdinalIgnoreCase)
			{
			{"/files/config.json", new ResourceInfo {
				uri = "/files/config.json", name = "Configuration File", description = "Server-side configuration in JSON format", mimeType = "application/json"
			}},
			{"/images/logo.png", new ResourceInfo {
				uri = "/images/logo.png", name = "Logo Image", description = "Company logo", mimeType = "image/png"
			}}
            // Add other static resources here
        };

			private readonly Dictionary<string, ResourceTemplateInfo> _resourceTemplates = new Dictionary<string, ResourceTemplateInfo>(StringComparer.OrdinalIgnoreCase)
			{
			{"/logs/{date}", new ResourceTemplateInfo {
				uriTemplate = "/logs/{date}", name = "Log File by Date", description = "Retrieve logs for a specific date (YYYY-MM-DD)", mimeType = "text/plain"
			}}
            // Add other resource templates here
        };

			public class PromptArgument {
				public string name { get; set; }
				public string description { get; set; }
				public bool? required { get; set; } // Nullable for optional serialization
			}

			public class PromptContentTemplate {
				public string type { get; set; } = "text";
				public string text { get; set; } // Template string with {argName}
			}

			public class PromptMessageTemplate {
				public string role { get; set; } // "user", "assistant"
				public PromptContentTemplate content { get; set; }
			}

			public class PromptInfo {
				// Note: Using lowercase names to match expected JSON output via JavaScriptSerializer
				public string name { get; set; }
				public string description { get; set; }
				public List<PromptArgument> arguments { get; set; }
				public List<PromptMessageTemplate> messageTemplates { get; set; }
			}

			// --- Data Structures for MCP Resources ---

			public class ResourceInfo {
				// Using lowercase names for JSON serialization consistency
				public string uri { get; set; }
				public string name { get; set; }
				public string description { get; set; }
				public string mimeType { get; set; }
			}

			public class ResourceTemplateInfo {
				// Using lowercase names for JSON serialization consistency
				public string uriTemplate { get; set; }
				public string name { get; set; }
				public string description { get; set; }
				public string mimeType { get; set; }
			}

			// --- Helper classes for JSON RPC Responses ---
			// (Define these to make serialization more reliable than anonymous types)

			public class JsonRpcResponse<T> {
				public string jsonrpc { get; set; } = "2.0";
				public object id { get; set; }
				public T result { get; set; }
			}

			public class JsonRpcErrorResponse {
				public string jsonrpc { get; set; } = "2.0";
				public object id { get; set; }
				public JsonRpcError error { get; set; }
			}

			public class JsonRpcError {
				public int code { get; set; }
				public string message { get; set; }
				public object data { get; set; } // Optional
			}

			public class PromptsListResult {
				public List<PromptInfo> prompts { get; set; }
			}

			public class PromptGetResult {
				public string description { get; set; }
				public List<object> messages { get; set; } // List of final message objects
			}

			public class ResourceListResult {
				public List<object> resources { get; set; } // Can contain ResourceInfo or ResourceTemplateInfo shapes
			}

			// Structure for the final substituted message
			public class FinalPromptMessage {
				public string role { get; set; }
				public FinalPromptContent content { get; set; }
			}

			public class FinalPromptContent {
				public string type { get; set; }
				public string text { get; set; }
			}
		}
	}
