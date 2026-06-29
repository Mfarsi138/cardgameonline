# Oh Hell AI Strategy Guide

## Overview

Oh Hell (also called Up and Down the River, Wizard, Nomination Whist, Bust) is an exact-bid trick-taking game. The AI must solve two distinct problems each round:
1. **Bidding**: Predict exactly how many tricks your hand will win
2. **Trick play**: Win exactly that many tricks — no more, no less

This document consolidates strategy rules from game theory, published AI research (MCTS, STS), human expert play, and the NeuralPlay implementation.

---

## 1. Bidding Strategy

### 1.1 Hand Evaluation

Classify every card in hand by suit and strength:

| Category | Trump Suit | Non-Trump |
|---|---|---|
| **Sure winner** | A, K, Q (top 3 trumps) | — |
| **Probable winner** | J, 10, 9 | A (ace), KQ+ in 4+ card suits |
| **Possible winner** | 8, 7, 6 | K (king), Q with length |
| **Loser** | 5 and below | Everything else |

**Trump hierarchy is inflated**: In a 7-card hand with 4 trumps, you may win 3-4 tricks from trumps alone. In a 13-card hand with 2 trumps, each trump is a near-sure trick.

### 1.2 Counting Sure Tricks

Walk through each suit and count:

1. **Trump Aces/Kings**: Count 1 each (near-certain winners)
2. **Trump length bonus**: If you hold 3+ trumps, add `(trumpCount - 2) × 0.5` — you can likely win with small trumps against side suits
3. **Side-suit Aces**: Count 1 if the suit has 2+ cards (if singleton, 0.5 — opponent may trump)
4. **Side-suit KQ in 4+ card suits**: Count 0.5 — opponent may trump on 3rd round
5. **Void suits**: Count 0.5 (opportunity to trump, but not guaranteed — depends on trump length)

### 1.3 Bidding Formula

```
sureTricks    = trumpAces + trumpKings + sideAcesNonSingleton
likelyTricks  = trumpLengthBonus + strongSideSuits + mediumTrump
possibleTricks = voidSuits + singletonAces/2 + lowTrump/2

baseEstimate = sureTricks + 0.7 × likelyTricks + 0.35 × possibleTricks
```

#### Adjustments

| Condition | Adjustment |
|---|---|
| Round size ≤ 3 cards | Bid more conservatively (randomness dominates) |
| Round size ≥ 10 cards | Bid more aggressively (skill dominates) |
| Dealer (hook rule) | If forced into impossible sum, choose the nearest safe bid |
| Already winning game | Slightly under-bid (reduce variance) |
| Already losing game | Slightly over-bid (increase variance) |
| Opponents have overbid | Bid conservatively — others will bust |
| Opponents have underbid | Bid aggressively — tricks are available |

#### The Hook Rule

When dealer, `total bids != cardsPerPlayer`. If the heuristic bid would break this, pick the nearest allowed value. Prefer under-bidding (it's easier to accidentally win than to discard a trick).

### 1.4 Zero Bid Strategy

Bidding 0 is **not a coward's exit** — it's a legitimate strategy:

- **Good zero bids**: 2-3 off-suit low cards, no trump, no Aces
- **Bad zero bids**: Any trump card, any Ace — you may win accidentally
- **Small rounds (1-3 cards)**: Bid zero aggressively with weak cards
- **Large rounds (10+ cards)**: Zero is nearly impossible — someone will lead a suit you must follow and you'll win

To execute zero: play your highest card in the led suit (dump), never trump, lead your lowest card when you win the lead.

### 1.5 Position-Based Bidding

- **Early position (first to bid)**: Most conservative — no information about others
- **Middle position**: Slightly more aggressive — can gauge early bids
- **Late position (dealer)**: Most information — adjust based on sum of bids so far
- If `sum of bids so far < tricks available - 2`, you have room to bid
- If `sum of bids so far > tricks available - 2`, bids are tight — dealer gets squeezed

---

## 2. Trick-Play Strategy

### 2.1 Core Decision: Win vs Dump

Each turn, the AI checks: `bid > tricksWon`?
- **Yes (need tricks)**: Try to win this trick
- **No (met/exceeded bid)**: Try to dump (lose) this trick

### 2.2 Leading Strategy

#### When NEEDING tricks (bid > tricksWon)

| Priority | Action | Rationale |
|---|---|---|
| 1 | Lead singleton Ace | Quick, safe trick |
| 2 | Lead from short strong suit (2 cards, KQ+) | Win now, void suit later |
| 3 | Lead trump from medium-strong trump holding | Draw opponents' trumps, establish control |
| 4 | Lead top card from longest non-trump suit | Establish long-suit winners |
| 5 | Lead lowest trump | If desperate — cheap trick |

**Don't**: Lead from a void suit (lets opponents trump), lead low from a strong suit (wastes the trick).

#### When DUMPING (bid ≤ tricksWon)

| Priority | Action | Rationale |
|---|---|---|
| 1 | Lead lowest card from longest non-trump suit | Safe, likely loses |
| 2 | Lead lowest non-trump from any suit | Safe loser |
| 3 | Lead highest trump | Sacrifice — ensures you won't accidentally win later |

**Don't**: Lead an Ace/King, lead from a void suit (opponent may trump and you avoid winning).

### 2.3 Following Strategy

#### When NEEDING tricks

| Situation | Play |
|---|---|
| Partner is winning | Play your LOWEST legal card (save high cards) |
| Opponent is winning, you can beat them | Play the CHEAPEST winning card (lowest card that beats current best) |
| Opponent is winning, you can't beat them | Play LOWEST card in suit (save for later) |
| Can't follow suit, must trump | Trump with LOWEST trump possible |
| Can't follow suit, can under-trump | Only under-trump if you're sure partner won't over-trump you |

**Key principle**: "Win cheaply" — don't waste an Ace to win a trick that a 9 would take. Save high trumps for later tricks.

#### When DUMPING

| Situation | Play |
|---|---|
| Can follow suit | Play your HIGHEST card in the led suit (dump the winner) |
| Can't follow suit, have trump | Trump with HIGHEST trump (dump it, opponent over-trump is fine) |
| Can't follow suit, no trump | Play HIGHEST off-suit card |
| Partner is winning | Play HIGHEST card — partner's win doesn't hurt you |
| Opponent winning, you must play | Play LOWEST card — don't accidentally help |

**Key principle**: When dumping, your goal is to **lose the trick**. Play cards that others will beat.

### 2.4 Trump Management

- **Count trumps played**: After 2-3 tricks, estimate how many trumps remain
- **When trumps are exhausted**: Side-suit Aces become safe winners
- **When needing tricks**: Lead trump early to draw out opponents' trumps
- **When dumping**: Avoid trumping — play off-suit high cards instead
- **Don't over-trump your partner**: If partner has the winning card, play under

### 2.5 End-Game Play (Last 2-3 Tricks)

- **Fewer cards → higher variance**: A lucky lead can steal a trick you don't want
- **If you need exactly 1 more trick**: Play conservatively — take the first available safe trick then dump everything else
- **If you need 0 more tricks**: Lead your lowest cards; never trump; play high when following suit
- **In 1-card hands**: If leading and you have the trump suit or a high card → likely win. If following suit, probability depends on the lead card

### 2.6 Card Counting

Track which cards have been played per suit:

- **High cards (A,K,Q,J)**: Know when a suit is "safe" (no more high cards left)
- **Trump cards**: Know when trump is exhausted (your side-suit winners become safe)
- **Inferred from bidding**: Players who bid high likely have trump and voids
- **Inferred from play**: A player who leads a low card in a new suit likely has that suit's Ace

### 2.7 Partner Play (4+ Players)

When partner leads:
- If partner is winning, play under (don't over-trump)
- If partner is losing, play just enough to win (cheaply) and lead back to partner
- If partner needs tricks (from their bid), help them win

When opponent leads:
- If opponent is winning and you can't beat them, dump high cards
- If opponent is winning and you can beat them, re-evaluate: do you NEED this trick?

---

## 3. Advanced Concepts

### 3.1 MCTS Approach (from academic research)

Monte Carlo Tree Search with these parameters:
- **Selection**: UCB1 (Upper Confidence Bound) for node selection
- **Expansion**: Add child states for each legal card
- **Simulation**: Random playout to end of trick (with informed heuristic)
- **Backpropagation**: Update expected tricks won

MCTS outperformed Simple Tree Search (47 vs 17 avg points). Key insight: MCTS handles hidden information better by averaging over random opponent hands.

### 3.2 Opposite-Last Rule Exploitation

In 1-card rounds:
- If leading: Spade (trump) holders have ~65%+ win probability
- If following non-trump: Win probability = (cards below yours in that suit) / 51 × probability no one else beats you
- Heuristic: If you have a trump card, bid 1; otherwise bid 0

### 3.3 Psychological Strategy (Multi-Agent)

- **Track reputation**: Note players who consistently over-bid or under-bid
- **Adjust expectations**: Against aggressive bidders, bid more conservatively (they'll bust more)
- **Bid variance**: When far behind, take bigger risks (bid 4+ on marginal hands)
- **Score pressure**: When close to winning, bid to protect your lead (under-bid slightly)

---

## 4. Common AI Mistakes to Avoid

| Mistake | Fix |
|---|---|
| Over-bidding based on trump | Trump is powerful but not guaranteed — opponents may have more |
| Under-bidding low trumps | Even a low trump can win if played at the right moment |
| Wasting high cards | Play the cheapest winning card, not the strongest |
| Failing to dump | Once bid is met, aggressively avoid winning — lead low, play high |
| Not counting trumps | Track trumps played to know when side-suit Aces become safe |
| Leading from voids | Opponents trump your Ace — lose a sure trick |
| Over-trumping partner | If partner is winning, play under |
| Fixed bidding | Adjust bids based on round size, position, and game state |

---

## 5. Scoring Context for AI

The AI should understand the scoring system:

- **Exact bid (n tricks)**: `10 + n²` points — rewards high bids heavily
  - Bid 0 and make it: 10 points
  - Bid 5 and make it: 35 points
  - Bid 10 and make it: 110 points
- **Miss (m tricks, m ≠ n)**: `-5 × (1 + 2 + ... + |n-m|)` = `-5 × |n-m| × (|n-m| + 1) / 2`
  - Miss by 1: -5 points
  - Miss by 2: -15 points
  - Miss by 3: -30 points

**AI implication**: Under-bidding by 1 costs -5. Over-bidding by 1 costs -5 plus you win a trick you didn't want. Both are equally bad, but over-bidding also distorts trick distribution for others.

---

## 6. Implementation Priority

For immediate AI improvement, fix in this order:

1. **Bidding accuracy**: Improve hand evaluation (sure/likely/possible categories, not just trump strength)
2. **Win-cheaply logic**: When needing tricks, play the cheapest winning card (not strongest)
3. **Dump strategy**: When bid is met, actively avoid winning — lead low from long suits, play high when following
4. **Trump tracking**: Count trumps played, adjust side-suit expectations
5. **End-game awareness**: Last 2-3 tricks need special handling (higher variance)
6. **Position-based bidding**: Adjust bid based on seat position and known bids
7. **Zero-bid sophistication**: Recognize good zero-bid hands and execute flawlessly
