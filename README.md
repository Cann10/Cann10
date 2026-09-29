# Cann10

身近な不便や、学習中に感じた課題を題材にソフトウェアを制作しています。
WebのプロトタイプやWindows向けツールを通じて、画面・処理・データの設計を学んでいます。

## 制作物

### [StateInk](https://github.com/Cann10/StateInk)

状態遷移図を編集・実行し、行き止まりや到達できない状態を確認するWebツールです。

- **技術:** TypeScript / React / Python / FastAPI / OpenCV
- **実装:** 状態のシミュレーション、グラフ解析、BFSによる問題状態までの最短操作列の生成
- **設計:** 解析処理をUIから分離。画像認識の結果は人が確認・修正してから取り込む構成

### [sonotoki — そのとき](https://github.com/Cann10/sonotoki)

「帰宅したとき」「お店に着いたとき」など、状況に合わせてメモを表示する体験を検証したWebプロトタイプです。

- **技術:** TypeScript / React / Vite / Vitest
- **実装:** 日本語のルールベース解析、場所の登録、状況のシミュレーション
- **設計:** 文章の解釈・通知条件への変換・条件判定を分離し、画面とは独立してテストできる構成
- **状況:** Webプロトタイプとして開発終了

### [WebClassNotifier](https://github.com/Cann10/WebClassNotifier)

WebClassの未読メッセージ件数を定期確認し、増加時に通知するWindows向けツールです。

- **技術:** C++ / Qt 6 / CMake / SQLite
- **実装:** ログインとセッション管理、定期取得、前回値との比較、Windows・Discord通知
- **設計:** UI・通信・設定保存・通知の役割を分離

## 制作で扱っている技術

| 分野 | 言語・ツール |
| --- | --- |
| Web | TypeScript, React, Vite |
| バックエンド・画像処理 | Python, FastAPI, OpenCV |
| デスクトップ | C++, Qt 6, CMake, SQLite |
| 検証 | Vitest, pytest, Playwright |

各リポジトリのREADMEに、実装範囲・起動方法・制約をまとめています。
