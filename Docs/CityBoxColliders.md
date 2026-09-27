# PLATEAU都市モデル用Colliderツール

SampleSceneの当たり判定には`Assets/Generated/CityBoxColliders.prefab`を使用しています。現在はDEM地形に361個、建物407棟のうち398棟に計478個のBoxCollider、残る9棟に非凸MeshColliderを配置しています。BoxColliderを使う建物は最大でも1棟8個です。

## Unity Editorで再利用する方法

HierarchyでPLATEAU都市モデルのルートを選び、**Tools > Altitude Zero > City Box Colliders**から実行します。

- **Analyze Selected**：シーンを変更せず、生成予定のBoxCollider・MeshCollider数をConsoleに表示します。
- **Apply Selected**：選択した都市モデルの下に`__GeneratedBoxColliders`を作成します。再実行すると前回の生成物を置き換えます。SampleSceneでは重複を避けるため、配置済みの生成Prefabを削除します。実行後はシーンを保存してください。
- **Clear Selected**：生成したColliderを削除します。SampleSceneでは配置済みの生成Prefabも削除します。
- **Apply to SampleScene**：開いているSampleSceneの都市モデルに同じ処理を実行します。
- **Rebuild Sample Collider Prefab**：SampleSceneで使用中の`CityBoxColliders.prefab`を都市FBXから再生成します。シーン内のPrefab参照はそのまま使えます。現在のSampleSceneと同じく、都市モデルのルートTransformを原点・回転なし・等倍として計測します。

スクリプトは`Assets/Editor/CityBoxColliderTool.cs`です。BoxColliderへの当てはめにはMeshの頂点を読むため、FBXのインポート設定で**Read/Write**を有効にしてください。

## 当てはめの方法

建物はMeshの三角形をXZ平面に投影し、約10m単位の区画で形を調べます。隣接する区画をまとめてBoxColliderにするため、単純な建物は大きな箱1個、入り組んだ建物は少数の箱の組み合わせになります。1棟あたり32個を超える場合など、箱では形を保ちにくい場合はMeshColliderに切り替えます。細長く低ポリゴンの建物は、通路などの空間を大きな箱が塞ぐことがあるため、元のMeshに沿う非凸MeshColliderを使います。

Unityの`PolygonCollider`は2D用です。3Dで多角形の近似が必要な場合は、三角形64個以下で横方向の縦横比が3以下のMeshに凸形状のMeshColliderを使います。それ以外は元のMeshを使った非凸MeshColliderにします。これらは都市の静的なColliderとして生成し、実行時に形状を解析しません。

地面は`dem_*`の三角形から高さを計測します。基本は40m四方のBoxColliderで覆い、高低差が2mを超える場所だけ約10mまで分割します。地形は段状の近似なので、急斜面やタイルの境界はPlayモードで確認してください。
