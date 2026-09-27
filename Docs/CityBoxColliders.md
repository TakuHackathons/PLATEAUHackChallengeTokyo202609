# PLATEAU都市モデル用BoxColliderツール

SampleSceneの当たり判定には、`Assets/Generated/CityBoxColliders.prefab`を使用しています。内訳はDEM地形に361個、建物407棟のうち396棟に各1個で、**合計757個のBoxCollider**です。大きさの条件を満たさない11棟にはColliderを付けていません。以前の仮の平面Colliderは削除済みです。

## Unity Editorで再利用する方法

HierarchyでPLATEAU都市モデルのルートを選び、**Tools > Altitude Zero > City Box Colliders**から次のコマンドを実行します。

- **Analyze Selected**：シーンを変更せず、生成予定のCollider数をConsoleに表示します。
- **Apply Selected**：選択した都市モデルの下に`__GeneratedBoxColliders`を作成します。再実行すると前回の生成物を置き換えます。SampleSceneでは、Colliderの重複を防ぐため、配置済みの生成Prefabも削除します。実行後はシーンを保存してください。
- **Clear Selected**：生成したColliderを削除します。SampleSceneでは配置済みの生成Prefabも削除します。
- **Apply to SampleScene**：開いているSampleSceneから都市モデルを探し、同じ処理を実行します。

スクリプトは`Assets/Editor/CityBoxColliderTool.cs`です。EditorでMeshの形状を計測するため、FBXのインポート設定ではMeshの読み取りを有効にしてください。Colliderの生成はEditor上で行い、実行時にMeshColliderは追加しません。

地面は`dem_*`の三角形から高さを計測します。基本は40m四方のBoxColliderで覆い、計測した高低差が2mを超える場所だけ約10mまで分割します。各BoxColliderの上面には、計測点の高さの中央値を使います。地形は段状に近似されるため、急斜面、タイルの境界、FPSの開始位置はPlayモードで確認してください。

SampleSceneのFPS開始位置は`(0, 38.5, 110)`です。この位置の地面Collider上面は、およそ`y = 37.48`です。

建物は`bldg_*`ごとに全Meshの頂点から範囲を計測し、**建物1棟につき1個のBoxCollider**を作ります。水平方向には合計0.5mの余裕を持たせます。Meshを読み取れない建物、極端に小さい建物、幅または奥行きが80mを超える建物は、信頼できる箱を作れないため処理を飛ばします。建物内部の中庭や張り出しの下も箱で埋まるので、入口や狭い通路が必要な場所は個別に確認してください。
