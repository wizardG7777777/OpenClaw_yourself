"""
文本处理工具：关键词提取、相关性评分。
使用 jieba 分词支持中文，同时兼容英文。
"""

from __future__ import annotations

import re
import math
from collections import Counter

import jieba
import jieba.analyse

# 预加载词典，避免首次调用延迟
jieba.initialize()

# 中文停用词（高频无意义词）
_CN_STOPWORDS = frozenset(
    "的 了 是 在 我 有 和 就 不 人 都 一 一个 上 也 很 到 说 要 去 你 会 着 没有 看 好 "
    "自己 这 他 她 它 们 那 里 为 什么 吗 吧 呢 啊 哦 嗯 呀 把 被 让 给 从 但 而 "
    "与 及 或 如果 因为 所以 虽然 但是 然后 可以 这个 那个 已经 还是 只是 不是".split()
)


def extract_keywords(text: str, top_k: int = 8) -> list[str]:
    """
    从文本中提取关键词。中文使用 jieba TF-IDF 提取；
    短文本补充分词结果中的名词/动词。
    """
    if not text or not text.strip():
        return []
    keywords = jieba.analyse.extract_tags(text, topK=top_k, withWeight=False)
    if len(keywords) < 3:
        words = jieba.lcut(text)
        for w in words:
            w = w.strip()
            if len(w) >= 2 and w not in _CN_STOPWORDS and w not in keywords:
                keywords.append(w)
            if len(keywords) >= top_k:
                break
    return keywords


def compute_relevance(query: str, document: str) -> float:
    """
    计算查询文本与文档之间的相关性分数（0~1）。
    基于词重叠的简单 TF 匹配。
    """
    q_words = set(jieba.lcut(query)) - _CN_STOPWORDS
    d_words = set(jieba.lcut(document)) - _CN_STOPWORDS
    if not q_words or not d_words:
        return 0.0
    overlap = q_words & d_words
    return len(overlap) / (math.sqrt(len(q_words)) * math.sqrt(len(d_words)))
