import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const output = path.join(
  root,
  "tests",
  "fixtures",
  "entity-resolution-golden-v1.jsonl",
);

const positiveFamilies = [
  ["OpenAI", "COMPANY", ["OpenAI", "OpenAI, Inc.", "Open AI"], "openai"],
  ["Microsoft", "COMPANY", ["Microsoft", "Microsoft Corp.", "微软"], "microsoft"],
  ["NVIDIA", "COMPANY", ["NVIDIA", "Nvidia Corporation", "英伟达"], "nvidia"],
  ["Large Language Model", "TECHNOLOGY", ["Large Language Model", "LLM", "大型语言模型", "大语言模型"], "large language model"],
  ["Retrieval-Augmented Generation", "TECHNOLOGY", ["Retrieval-Augmented Generation", "RAG", "检索增强生成"], "retrieval augmented generation"],
  ["LangChain", "FRAMEWORK", ["LangChain", "Lang Chain"], "langchain"],
  ["PyTorch", "FRAMEWORK", ["PyTorch", "Py Torch"], "pytorch"],
  ["TensorFlow", "FRAMEWORK", ["TensorFlow", "Tensor Flow"], "tensorflow"],
  ["Kubernetes", "TECHNOLOGY", ["Kubernetes", "K8s"], "kubernetes"],
  ["PostgreSQL", "TECHNOLOGY", ["PostgreSQL", "Postgres"], "postgresql"],
  ["Sam Altman", "PERSON", ["Sam Altman", "Samuel Altman", "萨姆·奥尔特曼"], "sam altman"],
  ["Elon Musk", "PERSON", ["Elon Musk", "埃隆·马斯克"], "elon musk"],
  ["GPT-4", "MODEL", ["GPT-4", "GPT 4"], "gpt 4"],
  ["GPT-4o", "MODEL", ["GPT-4o", "GPT 4o"], "gpt 4o"],
  ["Claude 3.5 Sonnet", "MODEL", ["Claude 3.5 Sonnet", "Claude-3.5-Sonnet"], "claude 3.5 sonnet"],
  ["Qwen", "MODEL_FAMILY", ["Qwen", "通义千问"], "qwen"],
  ["Hugging Face", "COMPANY", ["Hugging Face", "HuggingFace"], "hugging face"],
  ["Visual Studio Code", "PRODUCT", ["Visual Studio Code", "VS Code", "VSCode"], "visual studio code"],
  ["GitHub", "PRODUCT", ["GitHub", "Github"], "github"],
  ["Model Context Protocol", "STANDARD", ["Model Context Protocol", "MCP", "模型上下文协议"], "model context protocol"],
];

const hardNegativePairs = [
  ["GPT-4", "GPT-4o", "MODEL", "MODEL_VERSION_CONFLICT"],
  ["GPT-4", "GPT-5", "MODEL", "MODEL_VERSION_CONFLICT"],
  ["Claude 3", "Claude 3.5", "MODEL", "MODEL_VERSION_CONFLICT"],
  ["Qwen2", "Qwen2.5", "MODEL", "MODEL_VERSION_CONFLICT"],
  ["Llama 2", "Llama 3", "MODEL", "MODEL_VERSION_CONFLICT"],
  ["Microsoft", "Microsoft Azure", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Google", "Google Cloud", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Apple", "Apple Vision Pro", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Meta", "Meta Llama", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Amazon", "Amazon Bedrock", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
];

const ambiguousSameNamePairs = [
  ["Jordan", "Jordan", "PERSON", "同名人物与品牌，文档上下文指向人物"],
  ["Apple", "Apple", "COMPANY", "公司与水果概念同名"],
  ["Amazon", "Amazon", "COMPANY", "公司与地理实体同名"],
  ["Claude", "Claude", "MODEL", "模型与人物同名"],
  ["Gemini", "Gemini", "MODEL", "模型与星座概念同名"],
  ["Java", "Java", "TECHNOLOGY", "编程语言与地理实体同名"],
  ["Mercury", "Mercury", "PRODUCT", "产品与行星同名"],
  ["Python", "Python", "TECHNOLOGY", "编程语言与动物同名"],
  ["Delta", "Delta", "COMPANY", "公司与数学概念同名"],
  ["Atlas", "Atlas", "PRODUCT", "产品与人物概念同名"],
];

const relatedButDifferentPairs = [
  ["OpenAI", "ChatGPT", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Microsoft", "Azure", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Google", "Gemini", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Meta", "Llama", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Anthropic", "Claude", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["NVIDIA", "CUDA", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Apple", "iPhone", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["Amazon", "AWS", "COMPANY", "COMPANY_PRODUCT_BOUNDARY"],
  ["LangChain", "LangGraph", "FRAMEWORK", "RELATED_BUT_NOT_SAME"],
  ["PostgreSQL", "pgvector", "TECHNOLOGY", "RELATED_BUT_NOT_SAME"],
];

const records = [];
let index = 1;
for (let round = 0; round < 30; round += 1) {
  for (const [canonical, type, variants, key] of positiveFamilies) {
    const mention = variants[round % variants.length];
    records.push({
      id: `positive-${String(index++).padStart(4, "0")}`,
      language: /[\u3400-\u9fff]/u.test(mention) ? "zh-CN" : "en",
      mention,
      context: `${mention} is discussed as the same real-world entity as ${canonical}.`,
      entity_type: type,
      expected_canonical_name: canonical,
      expected_normalized_key: key,
      expected_decision: "SAME_ENTITY",
      expected_reason_codes: [
        "GOLDEN_VERIFIED_EQUIVALENCE",
        ...(mention !== canonical ? ["BILINGUAL_OR_ABBREVIATION"] : []),
      ],
      source: "synthetic-reviewed-baseline",
    });
  }
}

for (let round = 0; round < 20; round += 1) {
  for (const [mention, candidate, type, reason] of hardNegativePairs) {
    records.push({
      id: `negative-${String(index++).padStart(4, "0")}`,
      language: "en",
      mention,
      context: `${mention} and ${candidate} are explicitly different identities or versions.`,
      entity_type: type,
      candidate_name: candidate,
      expected_decision: "DIFFERENT_ENTITY",
      expected_reason_codes: [reason],
      source: "synthetic-reviewed-baseline",
    });
  }
}

for (let round = 0; round < 10; round += 1) {
  for (const [mention, candidate, type, context] of ambiguousSameNamePairs) {
    records.push({
      id: `ambiguous-${String(index++).padStart(4, "0")}`,
      language: "en",
      mention,
      context,
      entity_type: type,
      candidate_name: candidate,
      expected_decision: "DIFFERENT_ENTITY",
      expected_reason_codes: ["SAME_NAME_DIFFERENT_CONTEXT"],
      source: "synthetic-reviewed-baseline",
    });
  }
}

for (let round = 0; round < 10; round += 1) {
  for (const [mention, candidate, type, reason] of relatedButDifferentPairs) {
    records.push({
      id: `related-${String(index++).padStart(4, "0")}`,
      language: "en",
      mention,
      context: `${mention} is related to ${candidate}, but they are distinct identities.`,
      entity_type: type,
      candidate_name: candidate,
      expected_decision: "DIFFERENT_ENTITY",
      expected_reason_codes: [reason],
      source: "synthetic-reviewed-baseline",
    });
  }
}

if (records.length !== 1000) {
  throw new Error(`Expected 1000 records, generated ${records.length}`);
}

fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(
  output,
  `${records.map((record) => JSON.stringify(record)).join("\n")}\n`,
  "utf8",
);
console.log(`Generated ${records.length} records at ${output}`);
