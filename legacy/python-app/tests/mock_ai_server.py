"""Runs server.py with the AI providers mocked, to test the full browser flow."""
import json, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import server
CALLS = []
def fake(url, headers, body, timeout=90):
    CALLS.append(url)
    open(os.environ["MOCK_LOG"], "a").write(json.dumps({"url": url, "model": body.get("model"), "sys_len": len(body.get("system", "")),
        "rf": "response_format" in body})+"\n")
    if "anthropic" in url:
        user = body["messages"][-1]["content"]
        if "JSON object only" in body.get("system", ""):
            txt = json.dumps({"findings": [{"title": "RED polos over-bought", "severity": "high", "insight": "P3 (600 units) Cancel — PDSTR bottom quartile.", "impactQty": 600}], "action": {"title": "Cancel P3 with BETA", "detail": "Raise cancellation this week."}})
        else:
            txt = "MOCK ANSWER to: " + user
        return 200, {"content": [{"type": "text", "text": txt}]}
    if "groq" in url:
        if os.environ.get("GROQ_NO_JSON") and "response_format" in body:
            return 400, {"error": {"message": "response_format json_object not supported for this model"}}
        return 200, {"choices": [{"message": {"content": json.dumps({"findings": [{"title": "Groq finding", "severity": "medium", "insight": "g"}], "action": {"title": "Groq action", "detail": "x"}})}}]}
    return 500, {}
server._post_json = fake
server.CFG["anthropic_api_key"] = "sk-test"
server.CFG["groq_api_key"] = os.environ.get("GROQ_KEY", "")
server.main()
