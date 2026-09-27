# PLATEAU都市モデル用Colliderツール

SampleSceneの当たり判定には`Assets/Generated/CityBoxColliders.prefab`を使用しています。現在はDEM地形に361個、建物407棟のうち398棟に計478個のBoxCollider、残る9棟に非凸MeshColliderを配置しています。BoxColliderを使う建物は最大でも1棟8個です。

## Unity Editorで再利用する方法

**Tools > Altitude Zero > City Collider Prefab Window**を開き、都市モデルのPrefabまたはFBXを`Prefab / FBX`欄へドラッグし、**Apply**を押します。ツールが子オブジェクトのMeshFilterとMeshRendererを調べ、`__GeneratedBoxColliders`の下に編集可能なColliderを生成します。同じPrefabへ再適用すると前回の生成物を置き換えます。

- `.prefab`を指定した場合は、そのPrefabの中にColliderを保存します。
- FBXなどのモデルPrefabを指定した場合は、元のFBXを変更せず、`Assets/Generated/CityColliderPrefabs/`へCollider付きPrefabを作ります。
- Apply時に`Assets/Editor/Generated/`へ再適用用C#スクリプトを出力します。モデルを再インポートした後などに、そのスクリプトの`Apply()`を実行すると同じ計測処理を再実行できます。結果の各BoxColliderのCenter/SizeはPrefabに保存されます。

現在の都市FBXでWindowのApplyを実行した例は、[Collider付きPrefab](../Assets/Generated/CityColliderPrefabs/13104_shinjuku-ku_pref_2025_citygml_1_op_WithColliders.prefab)と[再適用スクリプト](../Assets/Editor/Generated/CityColliderReplay_fee1e66bad70e404db93f4e8bd5e39ac.cs)です。地面361個・建物494個のBoxColliderが付き、MeshColliderは0個です。このPrefabはSampleSceneへ自動配置されません。SampleSceneで使う際は、既存の都市モデルと`CityBoxColliders`の重複を避けて置き換えてください。

従来のシーン選択用メニューも引き続き使用できます。

HierarchyでPLATEAU都市モデルのルートを選び、**Tools > Altitude Zero > City Box Colliders**から実行します。

- **Analyze Selected**：シーンを変更せず、生成予定のBoxCollider・MeshCollider数をConsoleに表示します。
- **Apply Selected**：選択した都市モデルの下に`__GeneratedBoxColliders`を作成します。再実行すると前回の生成物を置き換えます。SampleSceneでは重複を避けるため、配置済みの生成Prefabを削除します。実行後はシーンを保存してください。
- **Clear Selected**：生成したColliderを削除します。SampleSceneでは配置済みの生成Prefabも削除します。
- **Apply to SampleScene**：開いているSampleSceneの都市モデルに同じ処理を実行します。
- **Rebuild Sample Collider Prefab**：SampleSceneで使用中の`CityBoxColliders.prefab`を都市FBXから再生成します。シーン内のPrefab参照はそのまま使えます。現在のSampleSceneと同じく、都市モデルのルートTransformを原点・回転なし・等倍として計測します。

計測処理は`Assets/Editor/CityBoxColliderTool.cs`、Prefab指定用Windowは`Assets/Editor/CityColliderPrefabWindow.cs`です。細かくBoxを分割するにはMeshの頂点を読むため、FBXのインポート設定で**Read/Write**を有効にしてください。無効でもBoundsを使う大きめのBoxを生成できます。

## 当てはめの方法

建物はMeshの三角形をXZ平面に投影し、約10m単位の区画で形を調べます。隣接する区画をまとめてBoxColliderにするため、単純な建物は大きな箱1個、入り組んだ建物は少数の箱の組み合わせになります。Prefab Windowでは箱を優先し、細長い建物や細分化しきれない建物も大きめのBoxColliderで覆います。Meshの頂点が読み取れない場合も、MeshまたはMeshRendererのBoundsからBoxColliderを作ります。有効なBoundsがない場合に限りMeshColliderを使います。従来のSampleScene用生成メニューは、細長い低ポリゴンの建物などを非凸MeshColliderにする設定です。

Unityの`PolygonCollider2D`は2D物理用で、FPSの3D当たり判定には使えません。Prefab Windowでは多角形の近似を複数のBoxColliderで行います。元のMeshを使うフォールバック以外にMeshColliderは生成しません。これらは都市の静的なColliderとして生成し、実行時に形状を解析しません。

地面は`dem_*`の三角形から高さを計測します。基本は40m四方のBoxColliderで覆い、高低差が2mを超える場所だけ約10mまで分割します。地形は段状の近似なので、急斜面やタイルの境界はPlayモードで確認してください。
